using System.Text.Json.Serialization;

namespace Fineage.Game;

public sealed partial class ClassCatalog
{
    sealed class ClassFileDto
    {
        public List<ClassDto>? Classes { get; set; }
    }

    sealed class ClassDto
    {
        public int ClassId { get; set; }
        public string? Name { get; set; }
        public int ParentClassId { get; set; } = -1;
        public int Race { get; set; }
        public int Str { get; set; }
        public int Dex { get; set; }
        public int Con { get; set; }
        [JsonPropertyName("int")] public int IntStat { get; set; }
        public int Wit { get; set; }
        public int Men { get; set; }
        public int PAtk { get; set; }
        public int MAtk { get; set; }
        public int PDef { get; set; }
        public int MDef { get; set; }
        public int Accuracy { get; set; }
        public int Crit { get; set; }
        public int PAtkSpd { get; set; }
        public int MAtkSpd { get; set; }
        public int RunSpeed { get; set; }
        public int WalkSpeed { get; set; }
        public CollisionDto? CollisionMale { get; set; }
        public CollisionDto? CollisionFemale { get; set; }
        public List<SpawnDto>? Spawns { get; set; }
        public List<double>? Hp { get; set; }
        public List<double>? Mp { get; set; }
        public List<double>? Cp { get; set; }
    }

    sealed class CollisionDto
    {
        public double Radius { get; set; }
        public double Height { get; set; }
    }

    sealed class SpawnDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    sealed class ExperienceFileDto
    {
        public int MaxLevel { get; set; }
        public int MaxPetLevel { get; set; }
        public List<ExperienceRowDto>? Levels { get; set; }
    }

    sealed class ExperienceRowDto
    {
        public int Level { get; set; }
        public long ToLevel { get; set; }
    }

    sealed class BonusFileDto
    {
        public List<double>? Str { get; set; }
        public List<double>? Dex { get; set; }
        public List<double>? Con { get; set; }
        [JsonPropertyName("int")] public List<double>? IntStat { get; set; }
        public List<double>? Wit { get; set; }
        public List<double>? Men { get; set; }
    }
}
