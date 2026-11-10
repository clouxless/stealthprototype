namespace StealthProto
{
    /// <summary>
    /// How lit the floor under the player is. Discrete on purpose: the player should always be
    /// able to tell which tier they're in by looking at the floor, and each tier has one fixed
    /// guard sight distance.
    /// </summary>
    public enum LightTier { Dark = 0, Dim = 1, Lit = 2 }
}
