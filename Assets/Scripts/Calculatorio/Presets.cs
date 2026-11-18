using System;
using System.Collections.Generic;
using UnityEngine;

namespace Calculatorio
{
    public static class Presets
    {
        // Common sector recipes (data-only)
        public static List<SectorRecipe> CommonRecipes()
{
    return new List<SectorRecipe>
    {
        new SectorRecipe {
            type=SectorType.Raw, name="Raw Extraction",
            alphaCapacity=0.9f, dimReturnK=0.5f,
            capacityGates=new[]{ Resource.ENERGY },
            io=new[]{
                new IO{ resource=Resource.RAW,       perTick=+140f },
                new IO{ resource=Resource.ENERGY,    perTick=-40f  },
                new IO{ resource=Resource.POLLUTION, perTick=+10f  }
            }
        },
        new SectorRecipe {
            type=SectorType.Energy, name="Power Generation",
            alphaCapacity=0.8f, dimReturnK=0.55f,
            capacityGates=new[]{ Resource.RAW, Resource.WATER },
            io=new[]{
                new IO{ resource=Resource.ENERGY,    perTick=+180f },
                new IO{ resource=Resource.RAW,       perTick=-60f  },
                new IO{ resource=Resource.WATER,     perTick=-20f  },
                new IO{ resource=Resource.POLLUTION, perTick=+8f   }
            }
        },
        new SectorRecipe {
            type=SectorType.Agriculture, name="Agriculture",
            alphaCapacity=0.8f, dimReturnK=0.6f,
            capacityGates=new[]{ Resource.WATER, Resource.ENERGY },
            io=new[]{
                new IO{ resource=Resource.FOOD,   perTick=+160f },
                new IO{ resource=Resource.WATER,  perTick=-70f  },
                new IO{ resource=Resource.ENERGY, perTick=-20f  }
            }
        },
        new SectorRecipe {
            type=SectorType.Industry, name="Industry",
            alphaCapacity=0.85f, dimReturnK=0.6f,
            capacityGates=new[]{ Resource.ENERGY },
            io=new[]{
                new IO{ resource=Resource.GOODS,     perTick=+150f },
                new IO{ resource=Resource.RAW,       perTick=-100f },
                new IO{ resource=Resource.ENERGY,    perTick=-40f  },
                new IO{ resource=Resource.POLLUTION, perTick=+10f  }
            }
        },
        new SectorRecipe {
            type=SectorType.Housing, name="Housing & Services",
            alphaCapacity=0.7f, dimReturnK=0.5f,
            capacityGates=new[]{ Resource.WATER, Resource.ENERGY },
            io=new[]{
                new IO{ resource=Resource.SERVICES, perTick=+140f },
                new IO{ resource=Resource.WATER,    perTick=-40f  },
                new IO{ resource=Resource.ENERGY,   perTick=-20f  }
            }
        },
        new SectorRecipe {
            type=SectorType.Education, name="Education",
            alphaCapacity=0.6f, dimReturnK=0.5f,
            capacityGates=new[]{ Resource.SERVICES },
            io=new[]{
                new IO{ resource=Resource.KNOWLEDGE, perTick=+60f },
                new IO{ resource=Resource.SERVICES,  perTick=-30f }
            }
        },
        new SectorRecipe {
            type=SectorType.Health, name="Health",
            alphaCapacity=0.6f, dimReturnK=0.5f,
            capacityGates=new[]{ Resource.SERVICES },
            io=new[]{
                new IO{ resource=Resource.SERVICES, perTick=-30f },
                new IO{ resource=Resource.WASTE,    perTick=-10f } // treat waste
            }
        },
        new SectorRecipe {
            type=SectorType.Transport, name="Transport",
            alphaCapacity=0.7f, dimReturnK=0.55f,
            capacityGates=new[]{ Resource.ENERGY },
            io=new[]{
                new IO{ resource=Resource.SERVICES, perTick=+60f },
                new IO{ resource=Resource.ENERGY,   perTick=-60f }
            }
        },
        new SectorRecipe {
            type=SectorType.Utilities, name="Utilities (Water/Waste)",
            alphaCapacity=0.7f, dimReturnK=0.55f,
            capacityGates=new[]{ Resource.ENERGY, Resource.WATER },
            io=new[]{
                new IO{ resource=Resource.WATER,  perTick=+100f },
                new IO{ resource=Resource.WASTE,  perTick=-50f  },
                new IO{ resource=Resource.ENERGY, perTick=-30f  }
            }
        },
        new SectorRecipe {
            type=SectorType.Environment, name="Environment",
            alphaCapacity=0.7f, dimReturnK=0.5f,
            capacityGates=System.Array.Empty<Resource>(),
            io=new[]{
                new IO{ resource=Resource.POLLUTION, perTick=-40f },
                new IO{ resource=Resource.MONEY,     perTick=-20f }
            }
        },
        new SectorRecipe {
            type=SectorType.Leisure, name="Leisure",
            alphaCapacity=0.6f, dimReturnK=0.5f,
            capacityGates=new[]{ Resource.SERVICES },
            io=new[]{
                new IO{ resource=Resource.SERVICES, perTick=-20f } // consumes to raise sat indirectly
            }
        },
        new SectorRecipe {
            type=SectorType.Government, name="Government/Tax",
            alphaCapacity=0.5f, dimReturnK=0.4f,
            capacityGates=System.Array.Empty<Resource>(),
            io=new[]{
                new IO{ resource=Resource.MONEY,    perTick=+120f },
                new IO{ resource=Resource.SERVICES, perTick=-40f  }
            }
        },
    };
}

        public static WorldState NewWorld(MapSize size, int seed = 0)
        {
            var w = new WorldState { map = size };
            // initialize all stock keys to 0
            foreach (Resource r in Enum.GetValues(typeof(Resource))) w.Set(r, 0f);

            var rnd = (seed == 0) ? new System.Random() : new System.Random(seed);

            // population & money baseline by map size
            if (size == MapSize.Planet)
            {
                w.Set(Resource.POP,   2_000_000_000f + rnd.Next(0, 200_000_000));
                w.Set(Resource.MONEY, 5_000_000f + rnd.Next(0, 500_000));
                w.trade.enabled = false;
            }
            else if (size == MapSize.Country)
            {
                w.Set(Resource.POP,   12_000_000f + rnd.Next(0, 5_000_000));
                w.Set(Resource.MONEY, 1_000_000f + rnd.Next(0, 200_000));
                w.trade.enabled = true;
            }
            else // City
            {
                w.Set(Resource.POP,   2_000_000f + rnd.Next(0, 1_000_000));
                w.Set(Resource.MONEY, 250_000f + rnd.Next(0, 50_000));
                w.trade.enabled = true;
            }

            float pop = w.population;
            float baseNeed = pop * 0.0015f;

            // Balanced RNG around needs; induce scarcities on smaller maps to make trade matter
            float scarcityFactorRAW   = size == MapSize.City    ? 0.4f : size == MapSize.Country ? 0.7f : 1.0f;
            float scarcityFactorWATER = size == MapSize.City    ? 0.7f : size == MapSize.Country ? 0.9f : 1.1f;
            float scarcityFactorENER  = size == MapSize.City    ? 0.6f : size == MapSize.Country ? 0.85f : 1.0f;
            float scarcityFactorFOOD  = size == MapSize.City    ? 0.6f : size == MapSize.Country ? 0.9f : 1.1f;

            w.Set(Resource.FOOD,      baseNeed * scarcityFactorFOOD  * (0.9f + 0.2f*(float)rnd.NextDouble()));
            w.Set(Resource.ENERGY,    baseNeed * scarcityFactorENER  * (0.9f + 0.2f*(float)rnd.NextDouble()));
            w.Set(Resource.WATER,     baseNeed * scarcityFactorWATER * (0.9f + 0.2f*(float)rnd.NextDouble()));
            w.Set(Resource.RAW,       baseNeed * scarcityFactorRAW   * (0.8f + 0.3f*(float)rnd.NextDouble()));
            w.Set(Resource.GOODS,     baseNeed * (0.8f + 0.4f*(float)rnd.NextDouble()));
            w.Set(Resource.SERVICES,  baseNeed * (0.9f + 0.3f*(float)rnd.NextDouble()));
            w.Set(Resource.WASTE,     baseNeed * (0.2f + 0.2f*(float)rnd.NextDouble()));
            w.Set(Resource.POLLUTION, baseNeed * (0.2f + 0.2f*(float)rnd.NextDouble()));
            w.Set(Resource.KNOWLEDGE, baseNeed * (0.6f + 0.3f*(float)rnd.NextDouble()));
            // LABOR derived each tick

            // Sectors & infra factors differ by map size
            var all = CommonRecipes();
            foreach (var def in all)
            {
                var st = new SectorState { def = def, intensity = 1f, infraFactor = 1f };
                if (size == MapSize.City)
                {
                    // City is infra-capped on heavy sectors
                    if (def.type == SectorType.Raw || def.type == SectorType.Energy || def.type == SectorType.Agriculture)
                        st.infraFactor = 0.6f;
                }
                else if (size == MapSize.Country)
                {
                    if (def.type == SectorType.Raw || def.type == SectorType.Energy)
                        st.infraFactor = 0.85f;
                }
                w.sectors[def.type] = st;
            }

            // Trade setup
            if (w.trade.enabled)
            {
                foreach (Resource r in Enum.GetValues(typeof(Resource)))
                {
                    w.trade.importPref[r] = 0f;
                    w.trade.exportPref[r] = 0f;
                }
                // defaults that make sense: likely imports for FOOD/ENERGY/WATER/RAW in smaller maps
                w.trade.importPref[Resource.FOOD]   = size == MapSize.City ? 0.8f : 0.4f;
                w.trade.importPref[Resource.ENERGY] = size == MapSize.City ? 0.7f : 0.4f;
                w.trade.importPref[Resource.WATER]  = size == MapSize.City ? 0.6f : 0.2f;
                w.trade.importPref[Resource.RAW]    = size == MapSize.City ? 0.7f : 0.3f;

                // export some surplus goods/services if any
                w.trade.exportPref[Resource.GOODS]    = 0.5f;
                w.trade.exportPref[Resource.SERVICES] = 0.3f;

                // world prices (tweak freely)
                w.trade.prices[Resource.FOOD] = 1.0f;
                w.trade.prices[Resource.ENERGY] = 1.4f;
                w.trade.prices[Resource.WATER] = 0.6f;
                w.trade.prices[Resource.RAW] = 0.9f;
                w.trade.prices[Resource.GOODS] = 1.6f;
                w.trade.prices[Resource.SERVICES] = 1.3f;
            }

            return w;
        }
    }
}
