using System;
using System.Collections.Generic;
using UnityEngine;

namespace Calculatorio
{
    public enum MapSize { City, Country, Planet }

    public enum Resource
    {
        POP, LABOR, FOOD, ENERGY, WATER, RAW, GOODS, SERVICES, MONEY, WASTE, POLLUTION, KNOWLEDGE
    }

    public enum SectorType
    {
        Raw, Energy, Agriculture, Industry, Housing, Education, Health, Transport, Utilities, Security, Environment, Leisure, Government
    }

    [Serializable]
    public struct IO
    {
        public Resource resource;   // + produces / - consumes per tick @ intensity=1
        public float perTick;
    }

    [Serializable]
    public class SectorRecipe
    {
        public SectorType type;
        public string name;
        public float alphaCapacity = 0.8f;  // capacity slope vs intensity
        public float dimReturnK   = 0.6f;   // diminishing returns (x>1)
        public Resource[] capacityGates;    // e.g., ENERGY gate for Industry
        public IO[] io;                     // signed flows per tick at x=1
    }

    [Serializable]
    public class SectorState
    {
        public SectorRecipe def;
        public float intensity = 1f;   // [0..2]
        public float infraFactor = 1f; // upgrades/progression hook
    }

    [Serializable]
    public class TradeState
    {
        public bool enabled;
        public Dictionary<Resource, float> importPref = new(); // 0..1 per resource
        public Dictionary<Resource, float> exportPref = new(); // 0..1 per resource
        public float importAggressiveness = 0.5f;  // fraction of deficit targeted per tick
        public float exportAggressiveness = 0.25f; // fraction of surplus sold per tick
        public Dictionary<Resource, float> prices = new();     // world prices (money units)
        public float friction = 0.9f; // export revenue multiplier (fees)
    }

    [Serializable]
    public class WorldState
    {
        public MapSize map;
        public Dictionary<Resource, float> stock = new();
        public Dictionary<SectorType, SectorState> sectors = new();
        public float satisfaction = 60f;
        public float priceLevel = 1f;
        public TradeState trade = new();

        public float population => Get(Resource.POP);
        public float labor      => Get(Resource.LABOR);

        public float Get(Resource r) => stock.TryGetValue(r, out var v) ? v : 0f;
        public void  Set(Resource r, float v) => stock[r] = Mathf.Max(0f, v);
        public void  Add(Resource r, float dv) => stock[r] = Mathf.Max(0f, Get(r) + dv);
    }

    public static class U
    {
        public static float Clamp01(float v) => Mathf.Clamp01(v);
        public static float SoftGate(float have, float k = 200f) => Mathf.Clamp01(have / (have + k));
        public static string Pretty(float v)
        {
            if (v >= 1_000_000_000f) return (v/1_000_000_000f).ToString("0.0")+"B";
            if (v >= 1_000_000f)     return (v/1_000_000f).ToString("0.0")+"M";
            if (v >= 1_000f)         return (v/1_000f).ToString("0.0")+"k";
            return v.ToString("0");
        }
    }
}
