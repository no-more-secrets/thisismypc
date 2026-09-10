using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Interop.Win32.Hardware.I2c;

/// <summary>
/// One I2C bus per NVIDIA GPU over NvAPI_I2CReadEx/WriteEx: port 1, the
/// GPU's PCI ids as the bus identity, SMBus transactions shaped exactly as
/// OpenRGB's i2c_smbus_nvapi.cpp shapes them. Loads NvAPI on first use; a
/// process that cannot load it gets no buses and a note saying why.
/// </summary>
public sealed class NvApiI2cBusProvider : II2cBusProvider, IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.NvApi");
    private readonly object _lock = new();
    private NvApi? _api;
    private bool _attempted;
    private string? _failure;

    public IReadOnlyList<II2cBus> Enumerate(List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        lock (_lock)
        {
            if (!_attempted)
            {
                _attempted = true;
                _api = NvApi.TryLoad(out _failure);
                if (_api is null)
                    Log.Info("NvAPI unavailable: {Reason}", _failure);
            }
            if (_api is null)
            {
                notes.Add($"NVIDIA GPU I2C: {_failure}");
                return [];
            }

            var buses = new List<II2cBus>();
            var gpus = _api.EnumeratePhysicalGpus();
            for (var i = 0; i < gpus.Length; i++)
            {
                var name = _api.FullName(gpus[i]) ?? "NVIDIA GPU";
                ushort vendor = 0, device = 0, subVendor = 0, subDevice = 0;
                if (_api.PciIdentifiers(gpus[i], out var deviceId, out var subSystemId))
                {
                    vendor = (ushort)(deviceId & 0xFFFF);
                    device = (ushort)(deviceId >> 16);
                    subVendor = (ushort)(subSystemId & 0xFFFF);
                    subDevice = (ushort)(subSystemId >> 16);
                }
                var info = new I2cBusInfo($"NVIDIA NvAPI I2C on GPU {i} ({name})", vendor, device, subVendor, subDevice, PortId: 1);
                notes.Add($"NVIDIA GPU I2C: {info.Name}, PCI {vendor:X4}:{device:X4} subsystem {subVendor:X4}:{subDevice:X4}.");
                buses.Add(new Bus(_api, gpus[i], info));
            }
            if (gpus.Length == 0)
                notes.Add("NVIDIA GPU I2C: NvAPI loaded but lists no GPU.");
            return buses;
        }
    }

    /// <summary>i2c_smbus_nvapi::i2c_smbus_xfer. One transaction at a time per GPU.</summary>
    private sealed class Bus(NvApi api, nint gpu, I2cBusInfo info) : II2cBus
    {
        private const int BlockMax = 32;
        private readonly object _lock = new();

        public I2cBusInfo Info => info;

        /// <summary>ENESMBusInterface_i2c_smbus::GetMaxBlock: three bytes, one LED, per block write.</summary>
        public int MaxBlock => 3;

        /// <summary>I2C_SMBUS_BYTE: no register byte on the wire (reg_addr_size 0), the value is data[0].</summary>
        public int ReadByte(byte address)
        {
            Span<byte> data = stackalloc byte[BlockMax];
            data[0] = 0;
            var status = Transfer(address, read: true, registerBytes: 0, register: 0, data, 1);
            return status == 0 ? data[0] : -1;
        }

        public int ReadByteData(byte address, byte command)
        {
            Span<byte> data = stackalloc byte[BlockMax];
            var status = Transfer(address, read: true, registerBytes: 1, register: command, data, 1);
            return status == 0 ? data[0] : -1;
        }

        public int WriteByteData(byte address, byte command, byte value)
        {
            Span<byte> data = [value];
            return Transfer(address, read: false, registerBytes: 1, register: command, data, 1) == 0 ? 0 : -1;
        }

        public int WriteWordData(byte address, byte command, ushort value)
        {
            Span<byte> data = [(byte)(value & 0xFF), (byte)(value >> 8)];
            return Transfer(address, read: false, registerBytes: 1, register: command, data, 2) == 0 ? 0 : -1;
        }

        /// <summary>I2C_SMBUS_BLOCK_DATA: the count byte precedes the data.</summary>
        public int WriteBlockData(byte address, byte command, ReadOnlySpan<byte> block)
        {
            if (block.Length == 0 || block.Length > BlockMax - 1)
                return -1;
            Span<byte> data = stackalloc byte[BlockMax];
            data[0] = (byte)block.Length;
            block.CopyTo(data[1..]);
            return Transfer(address, read: false, registerBytes: 1, register: command, data, block.Length + 1) == 0 ? 0 : -1;
        }

        /// <summary>
        /// One NvAPI_I2CReadEx or WriteEx, shaped as i2c_smbus_xfer shapes it:
        /// the register pointer always points at a byte (the reference keeps it
        /// valid even when reg_addr_size is 0), the data buffer is read back in
        /// place. Returns the NvAPI status; 0 is success.
        /// </summary>
        private unsafe int Transfer(byte address, bool read, uint registerBytes, byte register, Span<byte> data, int size)
        {
            lock (_lock)
            {
                fixed (byte* dataPointer = data)
                {
                    var info = new NvApi.NvI2cInfoV3
                    {
                        Version = NvApi.I2cInfoVersion,
                        DisplayMask = 0,
                        IsDdcPort = 0,
                        I2cDevAddress = (byte)(address << 1),
                        I2cRegAddress = &register,
                        RegAddrSize = registerBytes,
                        Data = dataPointer,
                        Size = (uint)size,
                        I2cSpeed = 0xFFFF,
                        I2cSpeedKhz = 0,
                        PortId = (byte)Info.PortId,
                        IsPortIdSet = 1,
                    };
                    return read ? api.I2cRead(gpu, ref info) : api.I2cWrite(gpu, ref info);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _api?.Dispose();
            _api = null;
        }
    }
}
