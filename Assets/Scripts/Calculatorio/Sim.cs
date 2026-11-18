using System;
using System.Collections.Generic;
using UnityEngine;
using static Calculatorio.U;

namespace Calculatorio
{
    public static class Sim
    {
        public static void Tick(WorldState w, float dt)
        {
            // 1) workforce
            float laborAvail = Workforce(w);
            w.Set(Resource.LABOR, laborAvail);

            // 2) sector flows
            var delta = NewZeroDict();
            foreach (var kv in w.sectors)
            {
                var st  = kv.Value;
                var def = st.def;
                float x = Mathf.Clamp(st.intensity, 0f, 2f);

                float cap = Capacity(w, st, x, laborAvail);
                float eff = 1f / (1f + def.dimReturnK * Mathf.Max(0f, x - 1f));
                float scale = Mathf.Min(cap, 1f) * eff * dt;

                for (int i = 0; i < def.io.Length; i++)
                {
                    var io = def.io[i];
                    float flow = io.perTick * x * scale; // signed
                    if (flow < 0f) // consumer: clamp by stock
                        flow = -Mathf.Min(-flow, w.Get(io.resource));
                    delta[io.resource] += flow;
                }
            }
            foreach (var kv in delta) w.Add(kv.Key, kv.Value);

            // 3) trade (for Country/City)
            if (w.trade.enabled) TradeSystem.TickTrade(w, dt);

            // 4) macro: prices, satisfaction, population
            UpdatePrices(w, dt);
            UpdateSatisfaction(w, dt);
            UpdatePopulation(w, dt);
        }

        static Dictionary<Resource, float> NewZeroDict()
        {
            var d = new Dictionary<Resource, float>();
            foreach (Resource r in Enum.GetValues(typeof(Resource))) d[r] = 0f;
            return d;
        }

        static float Workforce(WorldState w)
        {
            float basePart = 0.45f;
            float health = Clamp01(ScoreHealth(w));
            float edu    = Clamp01(ScoreEducation(w));
            float sat    = w.satisfaction / 100f;
            float participation = basePart * (0.8f + 0.2f*health) * (0.8f + 0.25f*edu) * (0.9f + 0.2f*sat);
            return w.population * participation;
        }

        static float Capacity(WorldState w, SectorState st, float x, float laborAvail)
        {
            float baseCap = 1f;
            float cap = baseCap * (1f + st.def.alphaCapacity * (x - 1f)) * st.infraFactor;

            var gates = st.def.capacityGates;
            if (gates != null)
                for (int i = 0; i < gates.Length; i++)
                    cap *= SoftGate(w.Get(gates[i]), 200f);

            cap *= Clamp01(laborAvail / (w.population * 0.5f + 1f));
            return Mathf.Clamp01(cap);
        }

        static void UpdatePrices(WorldState w, float dt)
        {
            float scarcity =
                Scarcity(w, Resource.FOOD,    0.0012f) * 0.35f +
                Scarcity(w, Resource.ENERGY,  0.0010f) * 0.25f +
                Scarcity(w, Resource.GOODS,   0.0010f) * 0.25f +
                Scarcity(w, Resource.SERVICES,0.0010f) * 0.15f;

            float target = Mathf.Lerp(1f, 1.8f, scarcity);
            w.priceLevel = Mathf.Lerp(w.priceLevel, target, 0.5f * dt);
        }

        static float Scarcity(WorldState w, Resource r, float coeff)
        {
            float want = coeff * w.population;
            float have = w.Get(r);
            return Mathf.Clamp01((want - have) / (want + 1f));
        }

        static void UpdateSatisfaction(WorldState w, float dt)
        {
            float housing = ScoreHousing(w);
            float food    = ScoreFood(w);
            float health  = ScoreHealth(w);
            float commute = ScoreTransport(w);
            float env     = ScoreEnvironment(w);
            float leisure = ScoreLeisure(w);
            float safety  = ScoreSecurity(w);
            float price   = Mathf.Clamp01((w.priceLevel - 1f) / 0.8f);

            float target =
                0.18f*housing + 0.18f*food + 0.14f*health + 0.12f*commute +
                0.12f*env + 0.10f*leisure + 0.08f*safety + 0.08f*(1f - price);

            w.satisfaction = Mathf.Lerp(w.satisfaction, Mathf.Clamp(target*100f, 0f, 100f), 0.4f*dt);
        }

        static void UpdatePopulation(WorldState w, float dt)
        {
            float sat = w.satisfaction/100f;
            float births = 0.002f * w.population * Mathf.Lerp(0.7f, 1.2f, sat);
            float deaths = 0.0016f * w.population * Mathf.Lerp(1.2f, 0.7f, sat) * (1f + 0.5f*PollutionPressure(w));
            float mig    = w.map == MapSize.Planet ? 0f : 0.001f * w.population * (sat - 0.5f);
            w.Set(Resource.POP, Mathf.Max(1000f, w.population + (births - deaths + mig) * dt));
        }

        // --- scoring helpers ---
        static float ScoreHousing(WorldState w)     => Clamp01(w.Get(Resource.SERVICES) / (0.0005f*w.population + 1));
        static float ScoreFood(WorldState w)        => Clamp01(w.Get(Resource.FOOD)     / (0.0008f*w.population + 1));
        static float ScoreHealth(WorldState w)      => Clamp01(w.Get(Resource.SERVICES) / (0.0003f*w.population + 1));
        static float ScoreTransport(WorldState w)   => Clamp01(w.Get(Resource.ENERGY)   / (0.0004f*w.population + 1));
        static float ScoreEnvironment(WorldState w) => Clamp01(1f - (w.Get(Resource.POLLUTION) / (w.population*0.002f + 1)));
        static float ScoreLeisure(WorldState w)     => Clamp01(w.Get(Resource.SERVICES) / (0.0006f*w.population + 1));
        static float ScoreSecurity(WorldState w)    => 0.8f;
        static float PollutionPressure(WorldState w)=> Clamp01(w.Get(Resource.POLLUTION) / (w.population*0.003f + 100f));
// === Add to Sim.cs inside the Sim class ===

// Fractional scale for a sector right now (like in Tick, but read-only)
public static float CurrentScale(WorldState w, SectorState st)
{
    float laborAvail = Workforce(w);                 // uses Sim's private helpers
    float x = Mathf.Clamp(st.intensity, 0f, 2f);
    float cap = Capacity(w, st, x, laborAvail);      // same gating as Tick()
    float eff = 1f / (1f + st.def.dimReturnK * Mathf.Max(0f, x - 1f));
    return Mathf.Min(cap, 1f) * eff;                 // fraction vs IO@x=1
}

// Per-sector instantaneous flows (approx) for current world state.
// Returns signed per-tick amounts by resource (positive=produce, negative=consume).
public static Dictionary<Resource, float> EstimateSectorFlows(WorldState w, SectorState st)
{
    float scale = CurrentScale(w, st);
    float x = Mathf.Clamp(st.intensity, 0f, 2f);
    var dict = new Dictionary<Resource, float>();
    foreach (var io in st.def.io)
    {
        float flow = io.perTick * x * scale;  // signed, per tick
        if (dict.TryGetValue(io.resource, out var acc)) dict[io.resource] = acc + flow;
        else dict[io.resource] = flow;
    }
    return dict;
}

        // missing earlier — now included
        static float ScoreEducation(WorldState w)
        {
            // Use KNOWLEDGE per capita as the proxy for education quality
            return Clamp01(w.Get(Resource.KNOWLEDGE) / (0.0004f*w.population + 1f));
        }
    }
}
