using System;
using UnityEngine;
using static Calculatorio.U;

namespace Calculatorio
{
    public static class TradeSystem
    {
        // Which resources are tradable in MVP
        static readonly Resource[] tradables = new[]
        {
            Resource.FOOD, Resource.ENERGY, Resource.WATER, Resource.RAW, Resource.GOODS, Resource.SERVICES
        };

        public static void TickTrade(WorldState w, float dt)
        {
            if (!w.trade.enabled) return;

            foreach (var r in tradables)
            {
                float price = w.trade.prices.TryGetValue(r, out var p) ? p : 1f;
                float want = WantPerCapita(r) * w.population; // target stock
                float have = w.Get(r);

                // Imports: cover fraction of deficit if we have money
                if (have < want)
                {
                    float deficit = want - have;
                    float pref = Clamp01(w.trade.importPref.TryGetValue(r, out var ip) ? ip : 0f);
                    float buy = deficit * pref * w.trade.importAggressiveness * dt;
                    float cost = buy * price * w.priceLevel;
                    if (cost > 0f)
                    {
                        float afford = Mathf.Min(buy, w.Get(Resource.MONEY) / Mathf.Max(0.0001f, price * w.priceLevel));
                        w.Add(r, afford);
                        w.Add(Resource.MONEY, -afford * price * w.priceLevel);
                    }
                }
                else
                {
                    // Exports: sell fraction of surplus beyond buffer
                    float buffer = want * 1.5f;
                    float surplus = Mathf.Max(0f, have - buffer);
                    if (surplus > 0f)
                    {
                        float pref = Clamp01(w.trade.exportPref.TryGetValue(r, out var ep) ? ep : 0f);
                        float sell = surplus * pref * w.trade.exportAggressiveness * dt;
                        if (sell > 0f)
                        {
                            w.Add(r, -sell);
                            w.Add(Resource.MONEY, sell * price * w.priceLevel * w.trade.friction);
                        }
                    }
                }
            }
        }

        static float WantPerCapita(Resource r)
        {
            // Simple per-capita targets that match the price update coeffs
            return r switch
            {
                Resource.FOOD => 0.0012f,
                Resource.ENERGY => 0.0010f,
                Resource.WATER => 0.0010f,
                Resource.GOODS => 0.0010f,
                Resource.SERVICES => 0.0010f,
                Resource.RAW => 0.0009f,
                _ => 0.0f
            };
        }
    }
}
