namespace Fineage.Protocol;

/// <summary>
/// Seven Signs wire. Opcodes sit clear of chat 8, inventory 15–17, party 18–19 and 26–27,
/// the 30–38 block, enchant 68, quests 80, and server replies 1040–1059.
/// Layout: <c>docs/seven-signs-v2-protocol.md</c>.
/// </summary>
public static class SevenSignsProtocol
{
    public const ushort StatusRequestOpcode = 90;
    public const ushort ActionRequestOpcode = 91;
    public const ushort StatusOpcode = 1090;
    public const ushort SkyOpcode = 1091;

    public const int CabalNone = 0;
    public const int CabalDusk = 1;
    public const int CabalDawn = 2;

    public const int SealNone = 0;
    public const int SealAvarice = 1;
    public const int SealGnosis = 2;
    public const int SealStrife = 3;

    public const int PeriodRecruiting = 0;
    public const int PeriodCompetition = 1;
    public const int PeriodResults = 2;
    public const int PeriodSealValidation = 3;

    public const int SkyNormal = 0;
    public const int SkyDusk = 1;
    public const int SkyDawn = 2;

    public const int ActionJoin = 1;
    public const int ActionContribute = 2;

    public const int BlueSealStoneId = 6360;
    public const int GreenSealStoneId = 6361;
    public const int RedSealStoneId = 6362;
    public const int BlueSealStoneValue = 3;
    public const int GreenSealStoneValue = 5;
    public const int RedSealStoneValue = 10;

    /// <summary>Certificate of Approval. Joining Dawn after the second class transfer can spend one.</summary>
    public const int CertificateOfApprovalId = 6388;

    public const int AdenaItemId = 57;

    /// <summary>SystemMessage 1040. Seal validation ended and recruitment is open. Above enchant 10–20 and clan 100–114.</summary>
    public const int PeriodRecruitingMessage = 120;

    /// <summary>SystemMessage 1040. Competition has begun.</summary>
    public const int PeriodCompetitionMessage = 121;

    /// <summary>SystemMessage 1040. Competition ended; results are being tallied.</summary>
    public const int PeriodResultsMessage = 122;

    /// <summary>SystemMessage 1040. Seal validation has begun.</summary>
    public const int PeriodValidationMessage = 123;

    public const int DawnWonMessage = 124;
    public const int DuskWonMessage = 125;
    public const int AvariceDawnMessage = 126;
    public const int AvariceDuskMessage = 127;
    public const int GnosisDawnMessage = 128;
    public const int GnosisDuskMessage = 129;
    public const int StrifeDawnMessage = 130;
    public const int StrifeDuskMessage = 131;
    public const int JoinedDawnMessage = 132;
    public const int JoinedDuskMessage = 133;
    public const int ChoseAvariceMessage = 134;
    public const int ChoseGnosisMessage = 135;
    public const int ChoseStrifeMessage = 136;

    public const string PeriodClosed = "seven_signs_period";
    public const string BadCabal = "seven_signs_cabal";
    public const string BadSeal = "seven_signs_seal";
    public const string ClassTooLow = "seven_signs_class";
    public const string CastleBlocked = "seven_signs_castle";
    public const string FeeRequired = "seven_signs_fee";
    public const string ContributionRejected = "seven_signs_contrib";
    public const string StonesMissing = "seven_signs_stones";
}
