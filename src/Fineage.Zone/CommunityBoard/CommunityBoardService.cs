using Fineage.Protocol;

namespace Fineage.Zone.CommunityBoard;

/// <summary>
/// Single Mobius-style board entry: open / parse / write and ShowBoard fanout.
/// Missing pages log the page name and do not invent HTML.
/// </summary>
public sealed class CommunityBoardService
{
    readonly CommunityBoardCatalog _pages;
    readonly Dictionary<int, string> _bypasses = new();
    readonly Dictionary<int, List<FavoriteRow>> _favorites = new();
    readonly Action<string> _warn;
    readonly Action<ShowBoardMsg> _send;

    /// <summary>Last ShowBoard content ids sent (tests).</summary>
    public List<string> LastSentContents { get; } = new();

    public CommunityBoardService(
        CommunityBoardCatalog pages,
        Action<ShowBoardMsg> send,
        Action<string>? warn = null)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _warn = warn ?? (_ => { });
    }

    public bool IsBoardCommand(string? command)
    {
        if (string.IsNullOrEmpty(command))
            return false;
        var c = command.Trim();
        return Starts(c, "_bbshome")
            || Starts(c, "_bbstop")
            || Starts(c, "_bbsgetfav")
            || Starts(c, "bbs_add_fav")
            || Starts(c, "_bbsdelfav_")
            || Starts(c, "_bbsloc")
            || Starts(c, "_bbsclan")
            || Starts(c, "_bbsmemo")
            || Starts(c, "_bbstopics")
            || Starts(c, "_bbsmail")
            || Starts(c, "_maillist")
            || Starts(c, "_bbsfriends")
            || Starts(c, "_friendlist")
            || Starts(c, "_friendblocklist")
            || Starts(c, "_bbslink");
    }

    public void ShowDefault(int characterId, string characterName, int clanCount) =>
        Parse(characterId, characterName, CommunityBoardProtocol.DefaultCommand, clanCount);

    public void Parse(int characterId, string characterName, string command, int clanCount)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        command = command.Trim();
        if (EqualsCmd(command, "_bbshome") || EqualsCmd(command, "_bbstop"))
        {
            Remember(characterId, "Home", command);
            SendPage(characterId, "home.html", html => html
                .Replace("%fav_count%", FavoriteCount(characterId).ToString(), StringComparison.Ordinal)
                .Replace("%region_count%", "9", StringComparison.Ordinal)
                .Replace("%clan_count%", clanCount.ToString(), StringComparison.Ordinal));
            return;
        }

        if (command.StartsWith("_bbstop;", StringComparison.OrdinalIgnoreCase))
        {
            var path = command["_bbstop;".Length..].Trim();
            if (path.Length > 0 && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                SendPage(characterId, path);
                return;
            }

            _warn($"[community-board] characterId={characterId} invalid _bbstop path command={command}");
            return;
        }

        if (Starts(command, "_bbsgetfav"))
        {
            Remember(characterId, "Favorites", command);
            SendFavorites(characterId);
            return;
        }

        if (Starts(command, "bbs_add_fav"))
        {
            AddFavorite(characterId);
            SendFavorites(characterId);
            return;
        }

        if (Starts(command, "_bbsdelfav_"))
        {
            var idText = command["_bbsdelfav_".Length..];
            if (int.TryParse(idText, out int favId))
                RemoveFavorite(characterId, favId);
            else
                _warn($"[community-board] characterId={characterId} invalid favorite id command={command}");
            SendFavorites(characterId);
            return;
        }

        if (EqualsCmd(command, "_bbsloc"))
        {
            Remember(characterId, "Region", command);
            SendRegion(characterId);
            return;
        }

        if (Starts(command, "_bbsloc;"))
        {
            Remember(characterId, "Region>", command);
            _warn($"[community-board] characterId={characterId} region detail not implemented command={command}");
            return;
        }

        if (Starts(command, "_bbsclan"))
        {
            Remember(characterId, "Clan", command);
            // Clan home HTML is built in Mobius ClanBoard (no static clan.html). Surface a named miss so
            // operators know the dynamic clan pages are still the gap — do not invent a screen.
            _warn($"[community-board] missing page=clan_home.html command={command} characterId={characterId}");
            return;
        }

        if (EqualsCmd(command, "_bbsmemo") || EqualsCmd(command, "_bbstopics"))
        {
            Remember(characterId, "Memo Command", command);
            SendPage(characterId, "memo.html");
            return;
        }

        if (EqualsCmd(command, "_bbsmail") || EqualsCmd(command, "_maillist"))
        {
            Remember(characterId, "Mail Command", command);
            SendPage(characterId, "mail.html");
            return;
        }

        if (EqualsCmd(command, "_bbsfriends") || EqualsCmd(command, "_friendlist"))
        {
            Remember(characterId, "Friends List", command);
            SendPage(characterId, "friends_list.html");
            return;
        }

        if (EqualsCmd(command, "_friendblocklist"))
        {
            Remember(characterId, "Ignore list", command);
            SendPage(characterId, "friends_block_list.html");
            return;
        }

        if (EqualsCmd(command, "_bbslink"))
        {
            SendPage(characterId, "homepage.html");
            return;
        }

        _warn($"[community-board] no parse handler for command={command} characterId={characterId}");
    }

    public void Write(int characterId, string characterName, string url, string arg1, string arg2, string arg3, string arg4, string arg5)
    {
        string cmd;
        switch (url)
        {
            case "Topic":
                cmd = "_bbstop";
                break;
            case "Post":
                SeparateAndSend(
                    $"<html><body><br><br><center>The command: {url} is not implemented yet.</center><br><br></body></html>");
                return;
            case "Region":
                cmd = "_bbsloc";
                break;
            case "Notice":
                cmd = "_bbsclan";
                break;
            default:
                SeparateAndSend(
                    $"<html><body><br><br><center>The command: {url} is not implemented yet.</center><br><br></body></html>");
                return;
        }

        // Mobius: write requires IWriteBoardHandler. Home (_bbstop) is parse-only → log.
        // Region / clan write bodies are TODO in Mobius CT0 — log args, do not invent.
        _warn(
            $"[community-board] write url={url} cmd={cmd} characterId={characterId} name={characterName} " +
            $"arg1={arg1} arg2={arg2} arg3={arg3} arg4={arg4} arg5={arg5}");
    }

    void SendFavorites(int characterId)
    {
        if (!_pages.TryGet("favorite_list.html", out var rowTemplate))
        {
            _warn($"[community-board] missing page=favorite_list.html characterId={characterId}");
            return;
        }

        if (!_pages.TryGet("favorite.html", out var shell))
        {
            _warn($"[community-board] missing page=favorite.html characterId={characterId}");
            return;
        }

        var sb = new System.Text.StringBuilder();
        if (_favorites.TryGetValue(characterId, out var rows))
        {
            foreach (var row in rows)
            {
                sb.Append(rowTemplate
                    .Replace("%fav_bypass%", row.Bypass, StringComparison.Ordinal)
                    .Replace("%fav_title%", row.Title, StringComparison.Ordinal)
                    .Replace("%fav_add_date%", row.AddedAt.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal)
                    .Replace("%fav_id%", row.Id.ToString(), StringComparison.Ordinal));
            }
        }

        SeparateAndSend(shell.Replace("%fav_list%", sb.ToString(), StringComparison.Ordinal));
    }

    void SendRegion(int characterId)
    {
        if (!_pages.TryGet("region_list.html", out var rowTemplate))
        {
            _warn($"[community-board] missing page=region_list.html characterId={characterId}");
            return;
        }

        if (!_pages.TryGet("region.html", out var shell))
        {
            _warn($"[community-board] missing page=region.html characterId={characterId}");
            return;
        }

        // Interlude region ids from Mobius RegionBoard.REGIONS — ownership filled once castles land.
        int[] regions = [1049, 1052, 1053, 1057, 1060, 1059, 1248, 1247, 1056];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < regions.Length; i++)
        {
            sb.Append(rowTemplate
                .Replace("%region_id%", i.ToString(), StringComparison.Ordinal)
                .Replace("%region_name%", regions[i].ToString(), StringComparison.Ordinal)
                .Replace("%region_owning_clan%", "NPC", StringComparison.Ordinal)
                .Replace("%region_owning_clan_alliance%", "", StringComparison.Ordinal)
                .Replace("%region_tax_rate%", "0%", StringComparison.Ordinal));
        }

        SeparateAndSend(shell.Replace("%region_list%", sb.ToString(), StringComparison.Ordinal));
    }

    void SendPage(int characterId, string page, Func<string, string>? transform = null)
    {
        if (!_pages.TryGet(page, out var html))
        {
            _warn($"[community-board] missing page={page} characterId={characterId}");
            return;
        }

        if (transform != null)
            html = transform(html);
        SeparateAndSend(html);
    }

    public void SeparateAndSend(string? html)
    {
        if (html == null)
            return;

        if (html.Length < CommunityBoardProtocol.ChunkChars)
        {
            Emit(CommunityBoardProtocol.HtmlId101, html);
            Emit(CommunityBoardProtocol.HtmlId102, null);
            Emit(CommunityBoardProtocol.HtmlId103, null);
            return;
        }

        if (html.Length < CommunityBoardProtocol.ChunkChars * 2)
        {
            Emit(CommunityBoardProtocol.HtmlId101, html[..CommunityBoardProtocol.ChunkChars]);
            Emit(CommunityBoardProtocol.HtmlId102, html[CommunityBoardProtocol.ChunkChars..]);
            Emit(CommunityBoardProtocol.HtmlId103, null);
            return;
        }

        if (html.Length < CommunityBoardProtocol.ChunkChars * 3)
        {
            Emit(CommunityBoardProtocol.HtmlId101, html[..CommunityBoardProtocol.ChunkChars]);
            Emit(CommunityBoardProtocol.HtmlId102,
                html[CommunityBoardProtocol.ChunkChars..(CommunityBoardProtocol.ChunkChars * 2)]);
            Emit(CommunityBoardProtocol.HtmlId103, html[(CommunityBoardProtocol.ChunkChars * 2)..]);
            return;
        }

        Emit(CommunityBoardProtocol.HtmlId101,
            "<html><body><br><center>Error: HTML was too long!</center></body></html>");
        Emit(CommunityBoardProtocol.HtmlId102, null);
        Emit(CommunityBoardProtocol.HtmlId103, null);
    }

    void Emit(string id, string? html)
    {
        var msg = new ShowBoardMsg
        {
            Show = 1,
            Content = CommunityBoardProtocol.ContentId(id, html)
        };
        LastSentContents.Add(msg.Content);
        _send(msg);
    }

    void Remember(int characterId, string title, string bypass) =>
        _bypasses[characterId] = title + "&" + bypass;

    void AddFavorite(int characterId)
    {
        if (!_bypasses.Remove(characterId, out var packed))
            return;
        var parts = packed.Split('&', 2);
        if (parts.Length != 2)
        {
            _warn($"[community-board] characterId={characterId} invalid favorite bypass={packed}");
            return;
        }

        if (!_favorites.TryGetValue(characterId, out var rows))
        {
            rows = new List<FavoriteRow>();
            _favorites[characterId] = rows;
        }

        rows.Add(new FavoriteRow(rows.Count + 1, parts[0].Trim(), parts[1].Trim(), DateTime.UtcNow));
    }

    void RemoveFavorite(int characterId, int favId)
    {
        if (!_favorites.TryGetValue(characterId, out var rows))
            return;
        rows.RemoveAll(r => r.Id == favId);
    }

    int FavoriteCount(int characterId) =>
        _favorites.TryGetValue(characterId, out var rows) ? rows.Count : 0;

    static bool Starts(string command, string prefix) =>
        command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    static bool EqualsCmd(string command, string exact) =>
        string.Equals(command, exact, StringComparison.OrdinalIgnoreCase);

    sealed record FavoriteRow(int Id, string Title, string Bypass, DateTime AddedAt);
}
