namespace Fineage.Protocol;

/// <summary>Mobius community board wire and page rules (Interlude <c>ShowBoard</c> / <c>CommunityBoardHandler</c>).</summary>
public static class CommunityBoardProtocol
{
    /// <summary>Mobius <c>Config.BBS_DEFAULT</c> default when unset.</summary>
    public const string DefaultCommand = "_bbshome";

    /// <summary>Mobius <c>Util.sendCBHtml</c> chunk size before splitting across 101/102/103.</summary>
    public const int ChunkChars = 16250;

    /// <summary>Retail <c>SystemMessageId.THE_COMMUNITY_SERVER_IS_CURRENTLY_OFFLINE</c>.</summary>
    public const int OfflineSm = 938;

    public const string OfflineText = "The community server is currently offline.";

    public const string TopBypass = "bypass _bbshome";
    public const string FavoriteBypass = "bypass _bbsgetfav";
    public const string RegionBypass = "bypass _bbsloc";
    public const string ClanBypass = "bypass _bbsclan";
    public const string MemoBypass = "bypass _bbsmemo";
    public const string MailBypass = "bypass _bbsmail";
    public const string FriendsBypass = "bypass _bbsfriends";
    public const string AddFavoriteBypass = "bypass bbs_add_fav";

    public const string HtmlId101 = "101";
    public const string HtmlId102 = "102";
    public const string HtmlId103 = "103";
    public const string HtmlId1001 = "1001";
    public const string HtmlId1002 = "1002";

    public static string ContentId(string id, string? html) =>
        id + "\u0008" + (html ?? "");
}
