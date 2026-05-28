using System.Collections.Generic;
using UnityEngine;

namespace WS_ProceduralGeneration
{
public class WSDG_Tier
{
    public enum Tier
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
        Cosmic,
        None
    }

    public static readonly Dictionary<Tier, float> BaseTierWeights = new()
    {
        { Tier.Common, 54f },
        { Tier.Uncommon, 25f },
        { Tier.Rare, 15f },
        { Tier.Epic, 4.999f },
        { Tier.Legendary, 1f },
        { Tier.Cosmic, 0.001f }
    };

    public static Dictionary<Tier, float> GetWeightedTiers(float multiplier)
    {
        Dictionary<Tier, float> modified = new ();

        foreach (var kvp in BaseTierWeights)
        {
            float weight = kvp.Value;

            switch (kvp.Key)
            {
                case Tier.Common:
                    weight /= multiplier;
                    break;

                case Tier.Uncommon:
                    weight /= Mathf.Sqrt(multiplier);
                    break;

                case Tier.Rare:
                    weight *= multiplier;
                    break;

                case Tier.Epic:
                    weight *= multiplier * multiplier;
                    break;

                case Tier.Legendary:
                    weight *= multiplier * multiplier * multiplier;
                    break;
            }

            modified[kvp.Key] = Mathf.Max(0.001f, weight);
        }

        return modified;
    }

    public static Tier RollRandomTier()
    {
        float total = 0f;
        foreach (var kvp in BaseTierWeights) total += kvp.Value;

        float roll = Random.Range(0f, total);
        foreach (var kvp in BaseTierWeights)
        {
            roll -= kvp.Value;
            if (roll <= 0f) return kvp.Key;
        }

        return Tier.Common;
    }
}

public interface IHaveTier
{
    public WSDG_Tier.Tier GetTier();
}
}
