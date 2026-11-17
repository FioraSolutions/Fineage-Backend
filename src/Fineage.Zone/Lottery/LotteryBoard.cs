using System.Numerics;
using System.Text.Json;
using Fineage.Protocol;

namespace Fineage.Zone.Lottery;

/// <summary>Mobius <c>General.ini</c> lottery defaults.</summary>
public sealed class LotterySettings
{
    public bool Enabled { get; init; } = true;
    /// <summary>Mobius <c>AltLotteryPrize</c>: the jackpot a fresh lottery starts with.</summary>
    public long InitialPrize { get; init; } = 50_000;
    public long TicketPrice { get; init; } = 2000;
    public double Rate5 { get; init; } = 0.6;
    public double Rate4 { get; init; } = 0.2;
    public double Rate3 { get; init; } = 0.2;
    /// <summary>Mobius <c>AltLottery2and1NumberPrize</c>: one or two matching numbers.</summary>
    public long SmallPrize { get; init; } = 200;
    /// <summary>Draw day and hour on the lottery clock (the ticket text says Saturday 7 pm).</summary>
    public DayOfWeek DrawDay { get; init; } = DayOfWeek.Saturday;
    public int DrawHour { get; init; } = 19;
    /// <summary>Lottery clock offset from UTC in minutes.</summary>
    public int UtcOffsetMinutes { get; init; }

    public static LotterySettings Mobius { get; } = new();

    public static LotterySettings FromEnvironment() => new()
    {
        Enabled = !string.Equals(Environment.GetEnvironmentVariable("FINEAGE_LOTTERY_ENABLED"), "0", StringComparison.Ordinal),
        UtcOffsetMinutes = int.TryParse(Environment.GetEnvironmentVariable("FINEAGE_LOTTERY_UTC_OFFSET_MINUTES"), out int off)
            ? Math.Clamp(off, -14 * 60, 14 * 60) : 0
    };
}

/// <summary>One finished drawing: the winning numbers (bit n−1 = number n) and the per-winner prizes.</summary>
public sealed class LotteryDraw
{
    public int Number { get; set; }
    public int Mask { get; set; }
    public long Jackpot { get; set; }
    public long Prize1 { get; set; }
    public long Prize2 { get; set; }
    public long Prize3 { get; set; }
    public int Winners1 { get; set; }
    public int Winners2 { get; set; }
    public int Winners3 { get; set; }
    public int Winners4 { get; set; }
}

/// <summary>
/// A sold ticket. The ticket item's enchant level carries <see cref="Serial"/>, so the ticket survives trades and
/// warehouses (Mobius keeps the numbers on the item's enchant / custom columns instead).
/// </summary>
public sealed class LotteryTicket
{
    public int Serial { get; set; }
    public int Number { get; set; }
    public int Mask { get; set; }
}

public sealed class LotterySnapshot
{
    public int Version { get; set; } = 1;
    public int Number { get; set; } = 1;
    public long Prize { get; set; }
    public long EndUnixMs { get; set; }
    public bool SuspendAnnounced { get; set; }
    public int NextSerial { get; set; } = 1;
    public List<LotteryDraw> Draws { get; set; } = [];
    public List<LotteryTicket> Tickets { get; set; } = [];
}

public interface ILotteryStore
{
    LotterySnapshot Load();
    void Save(LotterySnapshot snapshot);
}

public sealed class MemoryLotteryStore : ILotteryStore
{
    string _json = "";

    public LotterySnapshot Load() =>
        _json.Length == 0 ? new LotterySnapshot() : JsonSerializer.Deserialize<LotterySnapshot>(_json) ?? new LotterySnapshot();

    public void Save(LotterySnapshot snapshot) => _json = JsonSerializer.Serialize(snapshot);
}

public sealed class JsonLotteryStore(string path) : ILotteryStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly object _gate = new();

    public LotterySnapshot Load()
    {
        lock (_gate)
        {
            if (!File.Exists(path))
                return new LotterySnapshot();
            var data = JsonSerializer.Deserialize<LotterySnapshot>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"empty lottery store: {path}");
            if (data.Version != 1)
                throw new InvalidDataException($"unsupported lottery store version {data.Version}");
            data.Draws ??= [];
            data.Tickets ??= [];
            return data;
        }
    }

    public void Save(LotterySnapshot snapshot)
    {
        lock (_gate)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }
}

/// <summary>A board event the world announces to everyone online.</summary>
public readonly record struct LotteryNotice(int MessageId, string Text, string[] Params);

/// <summary>
/// Mobius <c>LotteryManager</c>: lottery #N sells tickets until ten minutes before the weekly draw, the draw picks
/// five distinct numbers of twenty, tickets of that lottery win by how many numbers they match (5 / 4 / 3 share
/// 60 / 20 / 20% of the jackpot left after the flat small prizes, 1–2 matches get the small prize), and whatever is
/// not won carries over to lottery #N+1. Mobius' carry-over subtracts one winner's share per tier, kept here.
/// </summary>
public sealed class LotteryBoard
{
    readonly LotterySettings _settings;
    readonly ILotteryStore _store;
    readonly LotterySnapshot _s;
    readonly Dictionary<int, LotteryTicket> _tickets = [];
    readonly Dictionary<int, LotteryDraw> _draws = [];

    public LotteryBoard(LotterySettings? settings = null, ILotteryStore? store = null, long? nowUnixMs = null)
    {
        _settings = settings ?? LotterySettings.Mobius;
        _store = store ?? new MemoryLotteryStore();
        _s = _store.Load();
        foreach (var t in _s.Tickets)
            _tickets[t.Serial] = t;
        foreach (var d in _s.Draws)
            _draws[d.Number] = d;
        if (_s.EndUnixMs == 0)
        {
            _s.Prize = _settings.InitialPrize;
            _s.EndUnixMs = NextDraw(nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Save();
        }
    }

    public static LotteryBoard OpenInStateDirectory(string stateDirectory, LotterySettings? settings = null)
    {
        string path = Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory, "lottery.json");
        return new LotteryBoard(settings ?? LotterySettings.FromEnvironment(), new JsonLotteryStore(path));
    }

    public LotterySettings Settings => _settings;
    public int Number => _s.Number;
    public long Prize => _s.Prize;
    public long EndUnixMs => _s.EndUnixMs;
    public IReadOnlyList<LotteryDraw> Draws => _s.Draws;

    public bool Selling(long now) => _settings.Enabled && now < _s.EndUnixMs - LotteryProtocol.SalesCloseMs;

    public LotteryTicket? Ticket(int serial) => _tickets.GetValueOrDefault(serial);

    public LotteryDraw? Draw(int number) => _draws.GetValueOrDefault(number);

    /// <summary>The next draw time strictly after <paramref name="afterUnixMs"/> on the lottery clock.</summary>
    public long NextDraw(long afterUnixMs)
    {
        var offset = TimeSpan.FromMinutes(_settings.UtcOffsetMinutes);
        var local = DateTimeOffset.FromUnixTimeMilliseconds(afterUnixMs).ToOffset(offset);
        int days = ((int)_settings.DrawDay - (int)local.DayOfWeek + 7) % 7;
        var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, _settings.DrawHour, 0, 0, offset).AddDays(days);
        if (candidate.ToUnixTimeMilliseconds() <= afterUnixMs)
            candidate = candidate.AddDays(7);
        return candidate.ToUnixTimeMilliseconds();
    }

    /// <summary>Numbers 1..20 → bit mask; null unless exactly five distinct valid numbers.</summary>
    public static int? MaskOf(IEnumerable<int> numbers)
    {
        int mask = 0, n = 0;
        foreach (int v in numbers)
        {
            if (v < 1 || v > LotteryProtocol.MaxNumber || (mask & (1 << (v - 1))) != 0)
                return null;
            mask |= 1 << (v - 1);
            n++;
        }
        return n == LotteryProtocol.Numbers ? mask : null;
    }

    public static int[] NumbersOf(int mask) =>
        Enumerable.Range(1, LotteryProtocol.MaxNumber).Where(v => (mask & (1 << (v - 1))) != 0).ToArray();

    /// <summary>Sells one ticket of the running lottery; the price joins the jackpot. Null when sales are closed or full.</summary>
    public LotteryTicket? Sell(int mask, long now)
    {
        if (!Selling(now) || BitOperations.PopCount((uint)mask) != LotteryProtocol.Numbers)
            return null;
        int serial = AllocateSerial();
        if (serial == 0)
            return null;
        var ticket = new LotteryTicket { Serial = serial, Number = _s.Number, Mask = mask };
        _tickets[serial] = ticket;
        _s.Tickets.Add(ticket);
        _s.Prize += _settings.TicketPrice;
        Save();
        return ticket;
    }

    int AllocateSerial()
    {
        for (int i = 0; i < 65535; i++)
        {
            int serial = (_s.NextSerial + i - 1) % 65535 + 1;
            if (!_tickets.ContainsKey(serial))
            {
                _s.NextSerial = serial % 65535 + 1;
                return serial;
            }
        }
        return 0;
    }

    /// <summary>
    /// Mobius <c>checkTicket</c>: tier 1–4 by matched numbers (5, 4, 3, 1–2) and its adena; (0, 0) for no match.
    /// Null while the ticket's lottery is not drawn yet or the serial is unknown.
    /// </summary>
    public (int Tier, long Adena)? Check(int serial)
    {
        if (!_tickets.TryGetValue(serial, out var ticket) || !_draws.TryGetValue(ticket.Number, out var draw))
            return null;
        return Tier(BitOperations.PopCount((uint)(ticket.Mask & draw.Mask)), draw);
    }

    (int, long) Tier(int matches, LotteryDraw draw) => matches switch
    {
        5 => (1, draw.Prize1),
        4 => (2, draw.Prize2),
        3 => (3, draw.Prize3),
        > 0 => (4, _settings.SmallPrize),
        _ => (0, 0)
    };

    /// <summary>Removes a claimed ticket (Mobius destroys the item whatever it won).</summary>
    public void Forget(int serial)
    {
        if (_tickets.Remove(serial, out var ticket))
        {
            _s.Tickets.Remove(ticket);
            Save();
        }
    }

    /// <summary>Sales close and draws come due; each finished draw starts the next lottery right away.</summary>
    public void Tick(long now, Random random, List<LotteryNotice> notices)
    {
        if (!_settings.Enabled)
            return;
        if (!_s.SuspendAnnounced && now >= _s.EndUnixMs - LotteryProtocol.SalesCloseMs)
        {
            _s.SuspendAnnounced = true;
            notices.Add(new(LotteryProtocol.SalesSuspended, "Lottery ticket sales have been temporarily suspended.", []));
            Save();
        }

        while (now >= _s.EndUnixMs)
        {
            Finish(random, notices);
            _s.EndUnixMs = NextDraw(Math.Max(now, _s.EndUnixMs));
            _s.SuspendAnnounced = now >= _s.EndUnixMs - LotteryProtocol.SalesCloseMs;
            notices.Add(new(0, $"Lottery tickets are now available for Lucky Lottery #{_s.Number}.", []));
            Save();
        }
    }

    void Finish(Random random, List<LotteryNotice> notices)
    {
        int mask = 0;
        for (int i = 0; i < LotteryProtocol.Numbers; i++)
        {
            int pick;
            do
                pick = random.Next(LotteryProtocol.MaxNumber) + 1;
            while ((mask & (1 << (pick - 1))) != 0);
            mask |= 1 << (pick - 1);
        }

        int c1 = 0, c2 = 0, c3 = 0, c4 = 0;
        foreach (var t in _s.Tickets.Where(t => t.Number == _s.Number))
        {
            switch (BitOperations.PopCount((uint)(t.Mask & mask)))
            {
                case 5: c1++; break;
                case 4: c2++; break;
                case 3: c3++; break;
                case > 0: c4++; break;
            }
        }

        long jackpot = _s.Prize;
        long prize4 = c4 * _settings.SmallPrize;
        long p1 = c1 > 0 ? (long)((jackpot - prize4) * _settings.Rate5 / c1) : 0;
        long p2 = c2 > 0 ? (long)((jackpot - prize4) * _settings.Rate4 / c2) : 0;
        long p3 = c3 > 0 ? (long)((jackpot - prize4) * _settings.Rate3 / c3) : 0;
        var draw = new LotteryDraw
        {
            Number = _s.Number, Mask = mask, Jackpot = jackpot, Prize1 = p1, Prize2 = p2, Prize3 = p3,
            Winners1 = c1, Winners2 = c2, Winners3 = c3, Winners4 = c4
        };
        _s.Draws.Add(draw);
        _draws[draw.Number] = draw;
        if (c1 > 0)
            notices.Add(new(LotteryProtocol.FirstPrizeWinners,
                $"The prize amount for the winner of Lottery #{_s.Number} is {jackpot} adena. We have {c1} first prize winners.",
                [_s.Number.ToString(), jackpot.ToString(), c1.ToString()]));
        else
            notices.Add(new(LotteryProtocol.NoFirstPrize,
                $"The prize amount for Lucky Lottery #{_s.Number} is {jackpot} adena. There was no first prize winner in this drawing, therefore the jackpot will be added to the next drawing.",
                [_s.Number.ToString(), jackpot.ToString()]));

        _s.Prize = Math.Max(0, jackpot - (p1 + p2 + p3 + prize4));
        _s.Number++;
    }

    void Save() => _store.Save(_s);
}
