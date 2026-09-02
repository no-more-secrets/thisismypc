using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>A 16x16 solid-color icon per file so the row's icon slot renders.</summary>
public sealed class UiFakeFileIconService : IFileIconService
{
    public OperationResult<FileIcon> GetSmallIcon(string path)
    {
        var pixels = new byte[16 * 16 * 4];
        var seed = (byte)(path.Length * 37);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(200 - seed);   // B
            pixels[i + 1] = (byte)(120 + seed / 2); // G
            pixels[i + 2] = seed;             // R
            pixels[i + 3] = 255;
        }
        return OperationResult<FileIcon>.Success(new FileIcon(16, 16, pixels));
    }
}

/// <summary>Signers by path fragment: Windows files verify as Microsoft Windows, Acme verifies, everything else is unsigned.</summary>
public sealed class UiFakeAuthenticodeService : IAuthenticodeService
{
    public SignatureInfo Check(string path)
    {
        if (path.Contains("Acme", StringComparison.OrdinalIgnoreCase))
            return new SignatureInfo(SignatureState.Verified, "Acme Inc.");
        if (path.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase))
            return new SignatureInfo(SignatureState.Verified, "Microsoft Windows");
        if (path.Contains("Twinkle", StringComparison.OrdinalIgnoreCase))
            return new SignatureInfo(SignatureState.NotVerified, "Xander Frangos");
        return SignatureInfo.Unsigned;
    }
}
