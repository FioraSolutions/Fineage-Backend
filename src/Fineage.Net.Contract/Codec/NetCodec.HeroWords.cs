namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeWriteHeroWords(WriteHeroWordsNetMsg m) => Write(w =>
        w.WriteString(m.Text ?? ""));

    public static WriteHeroWordsNetMsg DecodeWriteHeroWords(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new WriteHeroWordsNetMsg { Text = r.ReadString() };
    }
}
