using System.Text;
using System.Security.Principal;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Interop.Win32.Drift;

namespace ThisIsMyPC.Security.Tests.Drift;

public sealed class NativeRestorationEvidenceTests
{
    [Fact]
    public void ExactPreferenceAndRelatedPolicyRecordsBlockButUnrelatedPoliciesDoNot()
    {
        foreach (var target in RestorationCatalog.Default.Targets)
        {
            Assert.True(NativeRestorationEvidence.LocalPolicyTouchesTarget(Document(target.KeyPath[5..]), target.KeyPath));
            Assert.False(NativeRestorationEvidence.LocalPolicyTouchesTarget(Document(@"Software\Policies\Example"), target.KeyPath));
        }
        Assert.True(NativeRestorationEvidence.LocalPolicyTouchesTarget(Document(@"Software\Policies\Microsoft\Windows\CloudContent"),
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"));
        Assert.False(NativeRestorationEvidence.LocalPolicyTouchesTarget(Document(@"Software\Policies\Microsoft\Windows\CloudContent"),
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo"));
    }

    [Fact]
    public void MalformedOrTruncatedRecordsDoNotEstablishUnmanagedEvidence()
    {
        var document = Document(@"Software\Policies\Example");
        for (var length = 0; length < document.Length; length++)
        {
            if (length == 8) continue; // A valid empty document.
            var error = Record.Exception(() => NativeRestorationEvidence.LocalPolicyTouchesTarget(document[..length], @"HKCU\Example"));
            Assert.True(error is IOException or InvalidDataException);
        }
        document[0] = 0;
        Assert.Throws<InvalidDataException>(() => NativeRestorationEvidence.LocalPolicyTouchesTarget(document, @"HKCU\Example"));
    }

    [Fact, Trait("Category", "Diagnostic")]
    public void ReportCurrentOwnerEvidenceWithoutWritingPreferences()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        foreach (var target in RestorationCatalog.Default.Targets)
        {
            var evidence = new NativeRestorationEvidence().Read(new(target.ModuleId, target.SettingId, target.KeyPath, target.ValueName, sid));
            Console.WriteLine($"{target.SettingId}: {evidence.Profile?.State}, {evidence.Management?.State}");
            Assert.Equal(sid, evidence.Profile?.UserSid);
        }
    }

    private static byte[] Document(string key)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(0x67655250u); writer.Write(1u);
        writer.Write((ushort)'[');
        writer.Write(Encoding.Unicode.GetBytes(key + "\0")); writer.Write((ushort)';');
        writer.Write(Encoding.Unicode.GetBytes("Value\0")); writer.Write((ushort)';');
        writer.Write(4u); writer.Write((ushort)';');
        writer.Write(4u); writer.Write((ushort)';');
        writer.Write(1u); writer.Write((ushort)']');
        return stream.ToArray();
    }
}
