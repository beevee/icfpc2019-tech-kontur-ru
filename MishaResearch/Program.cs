﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lib;
using lib.Models;
using MongoDB.Bson.IO;
using Newtonsoft.Json;
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace MishaResearch
{
    class Program
    {
        static void Main(string[] args)
        {
            var moves = new List<V>{new V(-1,0), new V(1, 0), new V(0, -1), new V(0, 1), new V(-1, -1), new V(1, 1), new V(1, -1), new V(-1, 1) };

            Directory.CreateDirectory("maps2");

            var mapsPath = "../../../../problems/all/";
            foreach (var file in Directory.EnumerateFiles(mapsPath).Where(path => path.EndsWith(".desc")))
            {
                var problem = ProblemReader.Read(File.ReadAllText(file));
                var state = problem.ToState();
                var map = state.Map;

                var points = new List<GraphPoint>();
                var vecToId = new ConcurrentDictionary<V, int>();

                for (int y = 0; y < map.SizeY; y++)
                {
                    for (int x = 0; x < map.SizeX; x++)
                    {
                        var point = new V(x,y);
                        if(map[point] == CellState.Obstacle)
                            continue;
                        var gp = new GraphPoint { X = point.X, Y = point.Y, Id = vecToId.GetOrAdd(point, _ => vecToId.Count), ConnectedIds = new List<int>() };

                        for (int m = 0; m < moves.Count; m++)
                        {
                            var point2 = point + moves[m];

                            if (!point2.Inside(map) || map[point2] == CellState.Obstacle)
                                continue;
                            gp.ConnectedIds.Add(vecToId.GetOrAdd(point2, _ => vecToId.Count));
                        }
                        points.Add(gp);
                    }
                }

                var newFileName = "maps2/" + file.Substring(mapsPath.Length, file.Length - mapsPath.Length - 5) + ".graph";
                File.WriteAllLines(newFileName, points.Select(JsonConvert.SerializeObject));
            }
        }

        
    }

    class GraphPoint
    {
        public int X;
        public int Y;

        public int Id;
        public List<int> ConnectedIds;
    }
}