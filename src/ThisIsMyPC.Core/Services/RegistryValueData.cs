namespace ThisIsMyPC.Core.Services;

public enum RegistryValueDataKind
{
    String,
    ExpandString,
    Binary,
    DWord,
    QWord,
    MultiString,
}

/// <summary>
/// One registry value with its type, in a form that survives a JSON round
/// trip: strings as-is, multi-strings joined with '\0', numbers as decimal
/// text, binary as base64. Lets a value be moved between keys without
/// knowing its type up front (Autoruns-style AutorunsDisabled moves).
/// </summary>
public sealed record RegistryValueData(RegistryValueDataKind Kind, string Data)
{
    public static RegistryValueData FromString(string text) => new(RegistryValueDataKind.String, text);
    public static RegistryValueData FromExpandString(string text) => new(RegistryValueDataKind.ExpandString, text);
    public static RegistryValueData FromBinary(byte[] bytes) => new(RegistryValueDataKind.Binary, Convert.ToBase64String(bytes));
    public static RegistryValueData FromDWord(int value) => new(RegistryValueDataKind.DWord, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static RegistryValueData FromQWord(long value) => new(RegistryValueDataKind.QWord, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static RegistryValueData FromMultiString(string[] values) => new(RegistryValueDataKind.MultiString, string.Join('\0', values));

    public byte[] AsBinary() => Convert.FromBase64String(Data);
    public int AsDWord() => int.Parse(Data, System.Globalization.CultureInfo.InvariantCulture);
    public long AsQWord() => long.Parse(Data, System.Globalization.CultureInfo.InvariantCulture);
    public string[] AsMultiString() => Data.Length == 0 ? [] : Data.Split('\0');
}
