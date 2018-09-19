﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rhetos.Dsl;
using Rhetos.Extensibility;
using System.Globalization;
using Rhetos.Utilities;
using Rhetos.Compiler;
using Rhetos.Logging;
using System.Text;

namespace Rhetos.DatabaseGenerator
{
    public class DatabaseModelBuilder : IDatabaseModel
    {
        public List<NewConceptApplication> ConceptApplications => _conceptApplications.Value;
        private readonly Lazy<List<NewConceptApplication>> _conceptApplications;

        private readonly IPluginsContainer<IConceptDatabaseDefinition> _plugins;
        private readonly IDslModel _dslModel;
        private readonly ILogger _logger;
        private readonly ILogger _performanceLogger;

        public DatabaseModelBuilder(
            IPluginsContainer<IConceptDatabaseDefinition> plugins,
            IDslModel dslModel,
            ILogProvider logProvider)
        {
            _plugins = plugins;
            _dslModel = dslModel;
            _conceptApplications = new Lazy<List<NewConceptApplication>>(CreateNewApplications);
            _logger = logProvider.GetLogger(GetType().Name);
            _performanceLogger = logProvider.GetLogger("Performance");
        }

        protected List<NewConceptApplication> CreateNewApplications()
        {
            var stopwatch = Stopwatch.StartNew();

            var conceptApplications = new List<NewConceptApplication>();
            foreach (var conceptInfo in _dslModel.Concepts)
            {
                IConceptDatabaseDefinition[] implementations = _plugins.GetImplementations(conceptInfo.GetType()).ToArray();

                if (!implementations.Any())
                    implementations = new[] { new NullImplementation() };

                conceptApplications.AddRange(implementations.Select(impl => new NewConceptApplication(conceptInfo, impl))); // DependsOn, CreateQuery and RemoveQuery will be set later.
            }
            
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: Created concept applications from plugins.");

            ComputeDependsOn(conceptApplications);
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: Computed dependencies.");

            ComputeCreateAndRemoveQuery(conceptApplications, _dslModel.Concepts);
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: Generated SQL queries for new concept applications.");

            _logger.Trace(() => ReportDependencies(conceptApplications));

            return conceptApplications;
        }

        protected void ComputeDependsOn(IEnumerable<NewConceptApplication> newConceptApplications)
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var conceptApplication in newConceptApplications)
                conceptApplication.DependsOn = new ConceptApplicationDependency[] {};

            var dependencies = ExtractDependencies(newConceptApplications);
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: ExtractDependencies executed.");

            UpdateConceptApplicationsFromDependencyList(dependencies);
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: UpdateConceptApplicationsFromDependencyList executed.");
        }

        /// <summary>
        /// Updates ConceptApplication.DependsOn property from "flat" list of dependencies.
        /// </summary>
        protected static void UpdateConceptApplicationsFromDependencyList(IEnumerable<Dependency> dependencies)
        {
            var dependenciesByConceptApplication = dependencies
                .GroupBy(d => d.Dependent, d => new ConceptApplicationDependency { ConceptApplication = d.DependsOn, DebugInfo = d.DebugInfo });

            foreach (var dependencyGroup in dependenciesByConceptApplication)
            {
                var dependent = dependencyGroup.Key;
                var newDependsOn = dependencyGroup.Distinct().Union(dependent.DependsOn);

                dependent.DependsOn = newDependsOn.ToArray();
            }
        }

        protected IEnumerable<Dependency> ExtractDependencies(IEnumerable<NewConceptApplication> newConceptApplications)
        {
            var stopwatch = Stopwatch.StartNew();
            
            var exFromConceptInfo = ExtractDependenciesFromConceptInfos(newConceptApplications).ToList();
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: ExtractDependenciesFromConceptInfos executed.");
            
            var exFromMefPluginMetadata = ExtractDependenciesFromMefPluginMetadata(_plugins, newConceptApplications).ToList();
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: ExtractDependenciesFromMefPluginMetadata executed.");
            
            var combined = exFromConceptInfo.Union(exFromMefPluginMetadata).ToList();
            _performanceLogger.Write(stopwatch, "DatabaseGenerator.CreateNewApplications: Dependencies union executed.");
            
            return combined;
        }

        protected IEnumerable<Dependency> ExtractDependenciesFromConceptInfos(IEnumerable<NewConceptApplication> newConceptApplications)
        {
            var conceptInfos = newConceptApplications.Select(conceptApplication => conceptApplication.ConceptInfo).Distinct();

            var conceptInfoDependencies = conceptInfos.SelectMany(conceptInfo => conceptInfo.GetAllDependencies()
                .Select(dependency => Tuple.Create(dependency, conceptInfo, "Direct or indirect IConceptInfo reference")));

            return GetConceptApplicationDependencies(conceptInfoDependencies, newConceptApplications);
        }

        protected static IEnumerable<Dependency> GetConceptApplicationDependencies(IEnumerable<Tuple<IConceptInfo, IConceptInfo, string>> conceptInfoDependencies, IEnumerable<ConceptApplication> conceptApplications)
        {
            var conceptApplicationsByConceptInfoKey = conceptApplications
                .GroupBy(ca => ca.ConceptInfoKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            var conceptInfoKeyDependencies = conceptInfoDependencies.Select(dep => Tuple.Create(dep.Item1.GetKey(), dep.Item2.GetKey(), dep.Item3));

            var conceptApplicationDependencies =
                from conceptInfoKeyDependency in conceptInfoKeyDependencies
                where conceptApplicationsByConceptInfoKey.ContainsKey(conceptInfoKeyDependency.Item1)
                      && conceptApplicationsByConceptInfoKey.ContainsKey(conceptInfoKeyDependency.Item2)
                from dependsOnConceptApplication in conceptApplicationsByConceptInfoKey[conceptInfoKeyDependency.Item1]
                from dependentConceptApplication in conceptApplicationsByConceptInfoKey[conceptInfoKeyDependency.Item2]
                select new Dependency
                    {
                        DependsOn = dependsOnConceptApplication,
                        Dependent = dependentConceptApplication,
                        DebugInfo = conceptInfoKeyDependency.Item3
                    };

            return conceptApplicationDependencies.ToList();
        }

        protected static IEnumerable<Dependency> ExtractDependenciesFromMefPluginMetadata(IPluginsContainer<IConceptDatabaseDefinition> plugins, IEnumerable<NewConceptApplication> newConceptApplications)
        {
            var dependencies = new List<Dependency>();

            var conceptApplicationsByImplementation = newConceptApplications
                .GroupBy(ca => ca.ConceptImplementationType)
                .ToDictionary(g => g.Key, g => g.ToList());

            var distinctConceptImplementations = newConceptApplications.Select(ca => ca.ConceptImplementationType).Distinct().ToList();

            var implementationDependencies = GetImplementationDependencies(plugins, distinctConceptImplementations);

            foreach (var implementationDependency in implementationDependencies)
                if (conceptApplicationsByImplementation.ContainsKey(implementationDependency.Item1)
                    && conceptApplicationsByImplementation.ContainsKey(implementationDependency.Item2))
                    AddDependenciesOnSameConceptInfo(
                        conceptApplicationsByImplementation[implementationDependency.Item1],
                        conceptApplicationsByImplementation[implementationDependency.Item2],
                        implementationDependency.Item3,
                        dependencies);

            return dependencies.Distinct().ToList();
        }

        protected static IEnumerable<Tuple<Type, Type, string>> GetImplementationDependencies(IPluginsContainer<IConceptDatabaseDefinition> plugins, IEnumerable<Type> conceptImplementations)
        {
            var dependencies = new List<Tuple<Type, Type, string>>();

            foreach (Type conceptImplementation in conceptImplementations)
            {
                Type dependency = plugins.GetMetadata(conceptImplementation, "DependsOn");

                if (dependency == null)
                    continue;
                Type implements = plugins.GetMetadata(conceptImplementation, "Implements");
                Type dependencyImplements = plugins.GetMetadata(dependency, "Implements");

                if (!implements.Equals(dependencyImplements)
                    && !implements.IsAssignableFrom(dependencyImplements)
                    && !dependencyImplements.IsAssignableFrom(implements))
                    throw new FrameworkException(string.Format(
                        "DatabaseGenerator plugin {0} cannot depend on {1}."
                        + "\"DependsOn\" value in ExportMetadata attribute must reference implementation of same concept."
                        + " This additional dependencies should be used only to disambiguate between plugins that implement same IConceptInfo."
                        + " {2} implements {3}, while {4} implements {5}.",
                        conceptImplementation.FullName,
                        dependency.FullName,
                        conceptImplementation.Name,
                        implements.FullName,
                        dependency.Name,
                        dependencyImplements.FullName));

                dependencies.Add(Tuple.Create(dependency, conceptImplementation, "DependsOn metadata"));
            }

            return dependencies;
        }

        protected static void AddDependenciesOnSameConceptInfo(
            IEnumerable<ConceptApplication> applications1,
            IEnumerable<ConceptApplication> applications2,
            string debugInfo,
            List<Dependency> dependencies)
        {
            var applications2ByConceptInfoKey = applications2.ToDictionary(a => a.ConceptInfoKey);
            dependencies.AddRange(from application1 in applications1
                where applications2ByConceptInfoKey.ContainsKey(application1.ConceptInfoKey)
                select new Dependency
                    {
                        DependsOn = application1,
                        Dependent = applications2ByConceptInfoKey[application1.ConceptInfoKey],
                        DebugInfo = debugInfo
                    });
        }

        protected void ComputeCreateAndRemoveQuery(List<NewConceptApplication> newConceptApplications, IEnumerable<IConceptInfo> allConceptInfos)
        {
            Graph.TopologicalSort(newConceptApplications, ConceptApplication.GetDependencyPairs(newConceptApplications));

            var conceptInfosByKey = allConceptInfos.ToDictionary(ci => ci.GetKey());

            var sqlCodeBuilder = new CodeBuilder("/*", "*/");
            var createdDependencies = new List<Tuple<IConceptInfo, IConceptInfo, string>>();
            foreach (var ca in newConceptApplications)
            {
                AddConceptApplicationSeparator(ca, sqlCodeBuilder);

                // Generate RemoveQuery:

                GenerateRemoveQuery(ca);

                // Generate CreateQuery:

                sqlCodeBuilder.InsertCode(ca.ConceptImplementation.CreateDatabaseStructure(ca.ConceptInfo) + Environment.NewLine);

                if (ca.ConceptImplementation is IConceptDatabaseDefinitionExtension)
                {
                    IEnumerable<Tuple<IConceptInfo, IConceptInfo>> pluginCreatedDependencies;
                    ((IConceptDatabaseDefinitionExtension)ca.ConceptImplementation).ExtendDatabaseStructure(ca.ConceptInfo, sqlCodeBuilder, out pluginCreatedDependencies);

                    if (pluginCreatedDependencies != null)
                    {
                        var resolvedDependencies = pluginCreatedDependencies.Select(dep => Tuple.Create(
                            GetValidConceptInfo(dep.Item1.GetKey(), conceptInfosByKey, ca),
                            GetValidConceptInfo(dep.Item2.GetKey(), conceptInfosByKey, ca),
                            "ExtendDatabaseStructure " + ca.ToString())).ToList();
                        
                        createdDependencies.AddRange(resolvedDependencies);
                    }
                }
            }

            ExtractCreateQueries(sqlCodeBuilder.GeneratedCode, newConceptApplications);

            var createdConceptApplicationDependencies = GetConceptApplicationDependencies(createdDependencies, newConceptApplications);
            UpdateConceptApplicationsFromDependencyList(createdConceptApplicationDependencies);
        }

        public static void GenerateRemoveQuery(NewConceptApplication ca)
        {
            ca.RemoveQuery = ca.ConceptImplementation.RemoveDatabaseStructure(ca.ConceptInfo);
            if (ca.RemoveQuery != null)
                ca.RemoveQuery = ca.RemoveQuery.Trim();
            else
                ca.RemoveQuery = "";
        }

        protected static IConceptInfo GetValidConceptInfo(string conceptInfoKey, Dictionary<string, IConceptInfo> conceptInfosByKey, NewConceptApplication debugContextNewConceptApplication)
        {
            if (!conceptInfosByKey.ContainsKey(conceptInfoKey))
                throw new FrameworkException(string.Format(
                    "DatabaseGenerator error while generating code with plugin {0}: Extension created a dependency to the nonexistent concept info {1}.",
                    debugContextNewConceptApplication.ConceptImplementationType.Name,
                    conceptInfoKey));
            return conceptInfosByKey[conceptInfoKey];
        }

        protected const string NextConceptApplicationSeparator = "/*NextConceptApplication*/";
        protected const string NextConceptApplicationIdPrefix = "/*ConceptApplicationId:";
        protected const string NextConceptApplicationIdSuffix = "*/";

        protected static void AddConceptApplicationSeparator(ConceptApplication ca, CodeBuilder sqlCodeBuilder)
        {
            sqlCodeBuilder.InsertCode(string.Format("{0}{1}{2}{3}\r\n",
                NextConceptApplicationSeparator, NextConceptApplicationIdPrefix, ca.Id, NextConceptApplicationIdSuffix));
        }

        protected static void ExtractCreateQueries(string generatedSqlCode, IEnumerable<ConceptApplication> toBeInserted)
        {
            var sqls = generatedSqlCode.Split(new[] { NextConceptApplicationSeparator }, StringSplitOptions.None).ToList();
            if (sqls.Count > 0) sqls.RemoveAt(0);

            var toBeInsertedById = toBeInserted.ToDictionary(ca => ca.Id);

            int guidLength = Guid.Empty.ToString().Length;
            foreach (var sql in sqls)
            {
                var id = Guid.Parse(sql.Substring(NextConceptApplicationIdPrefix.Length, guidLength));
                toBeInsertedById[id].CreateQuery = sql
                    .Substring(NextConceptApplicationIdPrefix.Length + guidLength +NextConceptApplicationIdSuffix.Length)
                    .Trim();
            }
        }

        private string ReportDependencies(List<NewConceptApplication> conceptApplications)
        {
            var report = new StringBuilder();
            report.Append("Dependencies:");
            foreach (var ca in conceptApplications.Where(x => x.DependsOn.Any()))
            {
                report.AppendLine().Append(ca.ToString()).Append(" depends on:");
                foreach (var dep in ca.DependsOn)
                    report.Append("\r\n  ").Append(dep.ConceptApplication.ToString()).Append(" (").Append(dep.DebugInfo).Append(")");
            };
            return report.ToString();
        }
    }
}