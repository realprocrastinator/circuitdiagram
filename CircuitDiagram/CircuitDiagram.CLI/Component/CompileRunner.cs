﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CircuitDiagram.CLI.Component;
using CircuitDiagram.CLI.Component.OutputGenerators;
using CircuitDiagram.CLI.ComponentPreview;
using CircuitDiagram.Compiler;
using CircuitDiagram.IO;
using CircuitDiagram.TypeDescription;
using CircuitDiagram.TypeDescriptionIO.Xml;
using CircuitDiagram.TypeDescriptionIO.Xml.Logging;
using Microsoft.Extensions.Logging;

namespace CircuitDiagram.CLI.Component
{
    class CompileRunner
    {
        private readonly ILogger logger;
        private readonly IResourceProvider resourceProvider;

        public CompileRunner(ILogger logger, IResourceProvider resourceProvider)
        {
            this.logger = logger;
            this.resourceProvider = resourceProvider;
        }

        public CompileResult CompileOne(string inputFile, PreviewGenerationOptions previewOptions, IDictionary<IOutputGenerator, string> formats)
        {
            logger.LogInformation(inputFile);

            var loader = new XmlLoader();
            using (var fs = File.OpenRead(inputFile))
            {
                if (!loader.Load(fs, logger, out var description))
                {
                    Environment.Exit(1);
                }

                var outputs = Generate(fs, description, Path.GetFileNameWithoutExtension(inputFile), formats, previewOptions);

                var metadata = description.Metadata.Entries.ToDictionary(x => x.Key, x => x.Value);
                var svgIcon = GetSvgIconPath(Path.GetDirectoryName(inputFile), description);
                if (svgIcon != null)
                    metadata["org.circuit-diagram.icon-svg"] = svgIcon;
                
                return new CompileResult(description.Metadata.Author,
                                         description.ComponentName,
                                         description.Metadata.GUID,
                                         true,
                                         description.Metadata.AdditionalInformation,
                                         CleanPath(inputFile),
                                         metadata,
                                         outputs.ToImmutableDictionary());
            }
        }

        internal IEnumerable<KeyValuePair<string, string>> Generate(FileStream input,
                                                                    ComponentDescription description,
                                                                    string inputBaseName,
                                                                    IDictionary<IOutputGenerator, string> formats,
                                                                    PreviewGenerationOptions previewOptions)
        {
            foreach (var f in formats)
            {
                string format = f.Key.FileExtension.Substring(1);
                string autoGeneratedName = $"{inputBaseName}{f.Key.FileExtension}";
                string outputPath = f.Value != null && Directory.Exists(f.Value) ? Path.Combine(f.Value, autoGeneratedName) : f.Value ?? autoGeneratedName;
                using (var output = File.Open(outputPath, FileMode.Create))
                {
                    logger.LogDebug($"Starting {format} generation.");
                    input.Seek(0, SeekOrigin.Begin);
                    f.Key.Generate(description,
                                   resourceProvider,
                                   previewOptions,
                                   input,
                                   output);
                    logger.LogInformation($"  {format,-4} -> {outputPath}");
                }

                yield return new KeyValuePair<string, string>(format, outputPath);
            }
        }

        private static string GetSvgIconPath(string inputDirectory, ComponentDescription description)
        {
            inputDirectory = CleanPath(inputDirectory);

            var componentName = SanitizeName(description.ComponentName);

            if (!description.Metadata.Configurations.Any())
            {
                var icon = $"{componentName}.svg";
                if (Directory.EnumerateFiles(inputDirectory, icon).Any())
                {
                    return Path.Combine(inputDirectory, icon).Replace("\\", "/");
                }
            }

            foreach (var configuration in description.Metadata.Configurations)
            {
                var icon = $"{componentName}--{SanitizeName(configuration.Name)}.svg";
                if (Directory.EnumerateFiles(inputDirectory, icon).Any())
                {
                    return Path.Combine(inputDirectory, icon).Replace("\\", "/");
                }
            }

            return null;
        }

        private static string SanitizeName(string input)
        {
            var result = input.ToLowerInvariant();
            result = Regex.Replace(result, "[^a-z0-9]+", "_");
            if (result.EndsWith("_"))
                result = result.Substring(0, result.Length - 1);
            return result;
        }

        private static string CleanPath(string input)
        {
            var result = input.Replace("\\", "/").Replace("//", "/");
            if (result.StartsWith("./"))
                result = result.Substring(2);
            return result;
        }
    }
}
