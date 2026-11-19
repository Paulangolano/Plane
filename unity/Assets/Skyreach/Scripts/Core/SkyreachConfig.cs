// Central tuning values. Mirrors webxr/src/config.js so both versions fly the same.
namespace Skyreach
{
    public static class WorldConfig
    {
        public const int Seed = 1337;
        public const float Size = 6000f;
        public const float SeaLevel = 0f;
        public const float FogNear = 600f;
        public const float FogFar = 4200f;
        public const int IslandCount = 9;
        public const float TerrainResolution = 2f;
        public const int CloudCount = 70;
    }

    public static class FlightConfig
    {
        public const float MaxSpeed = 120f;
        public const float BoostSpeed = 170f;
        public const float MinFlySpeed = 38f;
        public const float TakeoffSpeed = 45f;
        public const float Accel = 0.2f;
        public const float PitchRate = 1.35f;
        public const float RollRate = 2.6f;
        public const float YawRate = 0.55f;
        public const float ControlSmoothing = 7f;
        public const float GravityClimbPenalty = 9.81f;
        public const float StallSinkRate = 14f;
        public const float GroundFriction = 0.35f;
        public const float BrakeFriction = 1.6f;
        public const float SafeTouchdownVSpeed = 7f;
        public const float SafeTouchdownRoll = 0.45f;
        public const float BoostDuration = 4f;
        public const float BoostRecharge = 0.35f;
        public const float GearHeight = 1.6f;
        public const float Ceiling = 3200f;
    }

    public static class CourseConfig
    {
        public const int RingCount = 12;
        public const float RingRadius = 22f;
        public const float RingTube = 1.6f;
    }

    public static class RunwayConfig
    {
        public const float Length = 900f;
        public const float Width = 40f;
        public const float Height = 6f;
    }
}
