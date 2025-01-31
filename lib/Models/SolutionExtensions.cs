using System;
using System.Collections.Generic;
using System.Linq;
using lib.Models.Actions;

namespace lib.Models
{
    public static class SolutionExtensions
    {
        public static string Format(this IEnumerable<ActionBase> actions)
        {
            return string.Join("", actions);
        }
        
        public static string Format(this IEnumerable<BoosterType> boosterTypes)
        {
            return string.Join("", boosterTypes.Select(x => new Booster(x, V.Zero).ToString()[0]));
        }

        public static string Format(this List<List<ActionBase>> solution)
        {
            return string.Join("#", solution.Select(x => x.Format()));
        }

        public static int CalculateTime(this List<List<ActionBase>> solution)
        {
            var ix = new List<int> {0};
            var result = 0;
            while (true)
            {
                var ixCount = ix.Count;
                var anyActed = false;
                for (var workerIndex = 0; workerIndex < ixCount; workerIndex++)
                {
                    var actionIndex = ix[workerIndex];
                    if (actionIndex >= solution[workerIndex].Count)
                        continue;
                    ix[workerIndex]++;
                    anyActed = true;
                    if (solution[workerIndex][actionIndex] is UseCloning)
                        ix.Add(0);
                }

                if (!anyActed)
                    break;

                result++;
            }

            return result;
        }
        
        public static BoosterType[] ToBuyBoosters(this string buy)
        {
            var boosterTypes = Enum.GetValues(typeof(BoosterType)).Cast<BoosterType>().Select(type => (new Booster(type, V.Zero).ToString()[0], type)).ToDictionary(x => x.Item1, x => x.type);
            return buy.Select(b => boosterTypes[b]).ToArray();
        }

    }
}