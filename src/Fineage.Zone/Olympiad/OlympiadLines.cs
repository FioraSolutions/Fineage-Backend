using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public static class OlympiadLines
{
    public static OlympiadNotice NotInProgress(int characterId) =>
        To(characterId, OlympiadProtocol.NotInProgress, "The Grand Olympiad Games are not currently in progress.");

    public static OlympiadNotice NoblesOnly(int characterId) =>
        To(characterId, OlympiadProtocol.NoblesOnly, "Only noblesse can participate in the Olympiad.");

    public static OlympiadNotice Subclass(int characterId) =>
        To(characterId, OlympiadProtocol.SubclassBlocked,
            "You cannot participate in the Grand Olympiad Games with a character in a subclass.");

    public static OlympiadNotice Cursed(int characterId) =>
        To(characterId, OlympiadProtocol.CursedWeapon, "You cannot participate in the Olympiad while you possess a cursed weapon.");

    public static OlympiadNotice Inventory(int characterId) =>
        To(characterId, OlympiadProtocol.InventoryBlocked,
            "You can't join a Grand Olympiad Game match with that much stuff on you. Reduce your weight to below 80 percent full and request to join again.");

    public static OlympiadNotice TooLate(int characterId) =>
        To(characterId, OlympiadProtocol.TooLate,
            "The request to participate in the game cannot be made starting from 10 minutes before the end of the game.");

    public static OlympiadNotice AlreadyClassed(int characterId) =>
        To(characterId, OlympiadProtocol.AlreadyClassed, "You have already joined the waiting list for a class specific match.");

    public static OlympiadNotice AlreadyRegistered(int characterId) =>
        To(characterId, OlympiadProtocol.AlreadyRegistered, "You have already joined the waiting list for a non-class specific match.");

    public static OlympiadNotice RegisteredClassed(int characterId) =>
        To(characterId, OlympiadProtocol.RegisteredClassed,
            "You have been registered in the Grand Olympiad Games waiting list for a class specific match.");

    public static OlympiadNotice RegisteredNonClassed(int characterId) =>
        To(characterId, OlympiadProtocol.RegisteredNonClassed,
            "You have been registered in the Grand Olympiad Games waiting list for a non-class specific match.");

    public static OlympiadNotice Unregistered(int characterId) =>
        To(characterId, OlympiadProtocol.Unregistered, "You have been removed from the Grand Olympiad Games waiting list.");

    public static OlympiadNotice Selected(int characterId) =>
        To(characterId, OlympiadProtocol.AlreadySelected, "Can't deregister whilst you are already selected for a game.");

    public static OlympiadNotice NotRegistered(int characterId) =>
        To(characterId, OlympiadProtocol.NotRegistered, "You are not currently registered on any Grand Olympiad Games waiting list.");

    public static OlympiadNotice NeedPoints(int characterId, int floor) =>
        To(characterId, OlympiadProtocol.NeedPoints, $"Cannot register when you have less than {floor} points.", floor.ToString());

    public static OlympiadNotice CompetitionStarted() =>
        All(OlympiadProtocol.CompetitionStarted,
            "Sharpen your swords, tighten the stitchings in your armor, and make haste to a Grand Olympiad Manager! Battles in the Grand Olympiad Games are now taking place!",
            "comp");

    public static OlympiadNotice CompetitionEnded() =>
        All(OlympiadProtocol.CompetitionEnded,
            "Much carnage has been left for the cleanup crew of the Olympiad Stadium. Battles in the Grand Olympiad Games are now over!",
            "comp");

    public static OlympiadNotice RegistrationEnded() =>
        All(OlympiadProtocol.RegistrationEnded, "The Grand Olympiad registration period has ended.", "registration");

    public static OlympiadNotice PeriodStarted(int cycle) =>
        All(OlympiadProtocol.CompetitionStarted, $"Period {cycle} of the Grand Olympiad Games has started.", "period", cycle.ToString());

    public static OlympiadNotice PeriodEnded(int cycle) =>
        All(OlympiadProtocol.CompetitionEnded, $"Period {cycle} of the Grand Olympiad Games has now ended.", "period", cycle.ToString());

    public static OlympiadNotice Countdown(int kind, int seconds) =>
        new(0, OlympiadProtocol.CountdownLine, CountdownText(kind, seconds), [kind.ToString(), seconds.ToString()]);

    public static OlympiadNotice Earned(string name, int points) =>
        new(0, OlympiadProtocol.PointsChanged, $"{name} has earned {points} points in the Grand Olympiad Games.", [name, points.ToString(), "earned"]);

    public static OlympiadNotice Lost(string name, int points) =>
        new(0, OlympiadProtocol.PointsChanged, $"{name} has lost {points} points in the Grand Olympiad Games.", [name, points.ToString(), "lost"]);

    public static OlympiadNotice Win(string name) =>
        new(0, OlympiadProtocol.MatchDecided, $"Congratulations {name}, you win the match.", [name]);

    public static OlympiadNotice Tie() =>
        new(0, OlympiadProtocol.MatchDecided, "There is no victor; the match ends in a tie.", ["tie"]);

    public static OlympiadNotice Cancelled() =>
        new(0, OlympiadProtocol.MatchDecided,
            "Your opponent does not meet the requirements to do battle. The match has been cancelled.",
            ["cancel"]);

    public static string CountdownText(int kind, int seconds) => kind switch
    {
        OlympiadProtocol.CountdownFight => $"The Grand Olympiad match will start in {seconds} second(s).",
        OlympiadProtocol.CountdownReturn => $"You will be moved back to town in {seconds} second(s).",
        _ => $"You will be moved to the Olympiad Stadium in {seconds} second(s)."
    };

    static OlympiadNotice To(int characterId, int id, string text, params string[] args) =>
        new(characterId, id, text, args);

    static OlympiadNotice All(int id, string text, params string[] args) =>
        new(0, id, text, args);
}
