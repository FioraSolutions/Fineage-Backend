using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class LanguageCodecTests
{
    [Fact]
    public void Auth_without_a_language_tail_still_decodes()
    {
        byte[] legacy = NetCodec.EncodeAuth(new AuthMsg
        {
            Account = "alice",
            Password = "secret1",
            RegisterIfMissing = true
        });
        var decoded = NetCodec.DecodeAuth(legacy);
        Assert.Equal("alice", decoded.Account);
        Assert.Equal("secret1", decoded.Password);
        Assert.True(decoded.RegisterIfMissing);
        Assert.Null(decoded.Language);
    }

    [Fact]
    public void Auth_language_is_an_optional_tail()
    {
        byte[] legacy = NetCodec.EncodeAuth(new AuthMsg { Account = "alice", Password = "secret1" });
        var withLanguage = new AuthMsg { Account = "alice", Password = "secret1", Language = "ru" };
        byte[] newer = NetCodec.EncodeAuth(withLanguage);

        Assert.True(newer.Length > legacy.Length);
        var decoded = NetCodec.DecodeAuth(newer);
        Assert.Equal("alice", decoded.Account);
        Assert.Equal("ru", decoded.Language);

        // An older decoder stops after the register flag, so the extra string is ignored.
        var older = NetCodec.DecodeAuth(legacy);
        Assert.Null(older.Language);
    }

    [Fact]
    public void EnterWorld_stays_empty_until_the_client_sends_a_language()
    {
        Assert.Empty(NetCodec.EncodeEnterWorld(new EnterWorldMsg()));
        Assert.Null(NetCodec.DecodeEnterWorld([]).Language);

        byte[] payload = NetCodec.EncodeEnterWorld(new EnterWorldMsg { Language = "pt-br" });
        Assert.Equal("pt-br", NetCodec.DecodeEnterWorld(payload).Language);
    }
}
