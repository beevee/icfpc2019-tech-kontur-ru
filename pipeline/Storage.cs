﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using lib.Models;
using lib.Solvers;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace pipeline
{
    public static class Storage
    {
        private const string dbHost = "mongodb://icfpc19-mongo1:27017";
        private const string dbName = "icfpc";
        private const string metaCollectionName = "solution_metas";
        private const string blockMetaCollectionName = "block_solution_metas";
        private const string solutionInProgressCollectionName = "solution_inprogress";
        private const string submissionSummaryCollectionName = "submission_summary";

        private static readonly MongoClient client = new MongoClient(dbHost);
        private static readonly IMongoDatabase database = client.GetDatabase(dbName);

        internal static readonly IMongoCollection<SolutionMeta> MetaCollection = database.GetCollection<SolutionMeta>(metaCollectionName);
        internal static readonly IMongoCollection<SolutionMeta> BlockMetaCollection = database.GetCollection<SolutionMeta>(blockMetaCollectionName);
        internal static readonly IMongoCollection<SolutionInProgress> SolutionInProgressCollection = database.GetCollection<SolutionInProgress>(solutionInProgressCollectionName);
        internal static readonly IMongoCollection<SubmissionSummary> SubmissionSummaryCollection = database.GetCollection<SubmissionSummary>(submissionSummaryCollectionName);

        public static List<SolutionMeta> EnumerateUnchecked()
        {
            return MetaCollection.FindSync(meta => !meta.IsOnlineChecked).ToList();
        }

        public static List<SolutionMeta> EnumerateSolved(ISolver solver)
        {
            return MetaCollection.FindSync(x => x.AlgorithmId == solver.GetName() && x.AlgorithmVersion == solver.GetVersion()).ToList();
        }

        public static List<SolutionMeta> EnumerateBestSolutions(int balance, double minDeltaCoeff)
        {
            var metas = new List<SolutionMeta>();

            var tuples = EnumerateBestSolutionTuples(minDeltaCoeff)
                .OrderByDescending(t => t.delta)
                .ToList();
            // var spendingLimit = 0;
            foreach (var tuple in tuples)
            {
                // if (balance >= tuple.best.MoneySpent && tuple.delta > 0)
                // {
                //     metas.Add(tuple.best);
                //     // spendingLimit--;
                // }
                // else
                    metas.Add(tuple.@base);

                balance -= metas.Last().MoneySpent;
            }

            return metas;
        }

        private static List<(SolutionMeta @base, SolutionMeta best, double delta)> EnumerateBestSolutionTuples(double minDeltaCoeff)
        {
            var metas = new List<(SolutionMeta @base, SolutionMeta best, double delta)>();
            var problemIds = MetaCollection.Distinct<int>("ProblemId", new BsonDocument()).ToList();

            foreach (var problemId in problemIds)
            {
                var map = ProblemReader.Read(problemId).ToState().Map;
                var mapScore = Math.Log(map.SizeX * map.SizeY, 2) * 1000;
                
                var pipeline = new[]
                {
                    new BsonDocument
                    {
                        {
                            "$match", new BsonDocument(
                                new Dictionary<string, object>
                                {
                                    {"ProblemId", problemId}
                                })
                        }
                    },
                    new BsonDocument()
                    {
                        {
                            "$group", new BsonDocument(
                                new Dictionary<string, object>
                                {
                                    {"_id", "$MoneySpent"},
                                    {
                                        "time", new BsonDocument(
                                            new Dictionary<string, string>
                                            {
                                                {"$min", "$OurTime"},
                                            })
                                    },
                                })
                        }
                    }
                };
                var minScoresForProblem = MetaCollection
                    .Aggregate<MinTimeResult>(pipeline)
                    .ToList();
                var baselineSolution = minScoresForProblem.First(s => s._id == 0);

                var estimatedSolutions = minScoresForProblem.Select(
                    s =>
                    {
                        var bestTime = s.time;
                        var baseScore = (int) Math.Ceiling(mapScore * bestTime / baselineSolution.time);
                        var score = (int) Math.Ceiling(mapScore * bestTime / s.time);
                        var scoreWithCost = score - s._id;

                        // var limit = mapScore - mapScore / minDeltaCoeff;
                        var delta = scoreWithCost - baseScore < 1000 ? -1 : (double)(baselineSolution.time - s.time) / baselineSolution.time;
                        
                        return new {s, delta};
                    })
                    .ToList();
                
                var optimalSolution = estimatedSolutions
                    .OrderByDescending(s => s.delta)
                    .First();

                var best = MetaCollection.FindSync(
                        y => y.ProblemId == problemId &&
                             y.OurTime == optimalSolution.s.time &&
                             y.MoneySpent == optimalSolution.s._id)
                    .First();
                var @base = MetaCollection.FindSync(
                        y => y.ProblemId == problemId &&
                             y.OurTime == baselineSolution.time &&
                             y.MoneySpent == baselineSolution._id)
                    .First();
                
                metas.Add((@base, best, optimalSolution.delta));
            }

            return metas;
        }

        public static void Remove(int problemId, string solverId, int ourTime)
        {
            MetaCollection.DeleteOne(
                y => y.ProblemId == problemId &&
                     y.OurTime == ourTime &&
                     y.AlgorithmId == solverId);
        }
    }

    internal class MinTimeResult
    {
#pragma warning disable 649
        public int _id;
        public int time;
#pragma warning restore 649
    }

    public static class StorageExtensions
    {
        public static void SaveToDb(this SolutionMeta meta, bool isBlockSolution = false)
        {
            if (meta.Id == ObjectId.Empty)
            {
                meta.Id = ObjectId.GenerateNewId();
            }

            meta.SavedAt = DateTimeOffset.Now.ToUnixTimeSeconds();
            var collection = isBlockSolution ? Storage.BlockMetaCollection : Storage.MetaCollection;
            collection.ReplaceOne(x => x.Id == meta.Id, meta, new UpdateOptions {IsUpsert = true});
        }

        public static void SaveToDb(this SolutionInProgress inProgress)
        {
            inProgress.StartedAt = DateTime.Now;
            inProgress.HostName = Environment.MachineName;
            Storage.SolutionInProgressCollection.InsertOne(inProgress);
        }

        public static void SaveToDb(this SubmissionSummary submissionSummary)
        {
            submissionSummary.GeneratedByHost = Environment.MachineName;
            submissionSummary.GeneratedAt = DateTime.Now;
            Storage.SubmissionSummaryCollection.ReplaceOne(
                s => s.ProblemId == submissionSummary.ProblemId,
                submissionSummary,
                new UpdateOptions {IsUpsert = true});
        }
    }
}