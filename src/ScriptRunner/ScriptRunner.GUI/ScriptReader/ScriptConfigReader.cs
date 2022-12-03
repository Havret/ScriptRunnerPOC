﻿using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Remote.Protocol.Viewport;
using ReactiveUI;
using ScriptRunner.GUI.ScriptConfigs;
using ScriptRunner.GUI.Settings;
using ScriptRunner.GUI.ViewModels;

namespace ScriptRunner.GUI.ScriptReader;

public static class ScriptConfigReader
{
    public static IEnumerable<ScriptConfig> Load(ConfigScriptEntry source)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
        {
            yield break;
        }
        
        if (source.Type == ConfigScriptType.File)
        {
            foreach (var scriptConfig in LoadFileSource(source.Path))
            {
                scriptConfig.SourceName = source.Name;
                yield return scriptConfig;
            }
            yield break;
        }

        if (source.Type == ConfigScriptType.Directory)
        {
            foreach (var file in Directory.EnumerateFiles(source.Path, "*.json", source.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                foreach (var scriptConfig in LoadFileSource(file))
                {
                    scriptConfig.SourceName = source.Name;
                    yield return scriptConfig;
                }
            }
            yield break;
        }
    }

    private static IAutoParameterBuilder CreateBuilder(ScriptConfig scriptConfig)
    {
        if (scriptConfig.AutoParameterBuilderStyle == "powershell")
        {
            return PowerShellAutoParameterBuilder.Instance;
        }

        if (string.IsNullOrWhiteSpace(scriptConfig.AutoParameterBuilderPattern) == false)
        {
            return new PatternBaseBuilderAutoParameterBuilder(scriptConfig.AutoParameterBuilderPattern);
        }

        return EmptyAutoParameterBuilder.Instance;
    }

    private static IEnumerable<ScriptConfig> LoadFileSource(string fileName)
    {
        if (!File.Exists(fileName)) return Array.Empty<ScriptConfig>();

        try
        {
            var jsonString = File.ReadAllText(fileName);

            if (jsonString.Contains("ScriptRunnerSchema.json") == false)
            {
                return Array.Empty<ScriptConfig>();
            }

            var scriptConfig = JsonSerializer.Deserialize<ActionsConfig>(jsonString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                Converters = { new PromptTypeJsonConverter(), new ParamTypeJsonConverter(), new JsonStringEnumConverter() }
            })!;

            foreach (var action in scriptConfig.Actions)
            {
                action.Source = fileName;
                var parameterBuilder = CreateBuilder(action);

                var autoGeneratedParameters = action.Params.Select(param => parameterBuilder.Build(param)).Where(paramString => string.IsNullOrWhiteSpace(paramString) == false);
                action.Command += " "+string.Join(" ", autoGeneratedParameters);

                if (string.IsNullOrWhiteSpace(action.WorkingDirectory))
                {
                    action.WorkingDirectory = Path.GetDirectoryName(fileName);
                }

                if (string.IsNullOrWhiteSpace(action.InstallCommandWorkingDirectory))
                {
                    action.InstallCommandWorkingDirectory = Path.GetDirectoryName(fileName);
                }

                var defaultSet = new ArgumentSet()
                {
                    Description = MainWindowViewModel.DefaultParameterSetName
                };

                foreach (var param in action.Params)
                {
                    defaultSet.Arguments[param.Name] = param.Default;
                }

                foreach (var set in action.PredefinedArgumentSets.Where(x => x.FallbackToDefault))
                {
                    foreach (var (key, val) in defaultSet.Arguments)
                    {
                        if (set.Arguments.ContainsKey(key) == false)
                        {
                            set.Arguments[key] = val;
                        }
                    }
                }

                switch (action.PredefinedArgumentSetsOrdering)
                {
                    case PredefinedArgumentSetsOrdering.Ascending:
                        action.PredefinedArgumentSets.Sort((s1, s2) => string.CompareOrdinal(s1.Description, s2.Description));
                        break;
                    case PredefinedArgumentSetsOrdering.Descending:
                        action.PredefinedArgumentSets.Sort((s1, s2) => string.CompareOrdinal(s2.Description, s1.Description));
                        break;
                }

                action.PredefinedArgumentSets.Insert(0, defaultSet);

                foreach (var param in action.Params.Where(x=>x.Prompt == PromptType.FileContent))
                {
                    foreach (var set in action.PredefinedArgumentSets)
                    {
                        if (set.Arguments.TryGetValue(param.Name, out var defaultValue))
                        {
                            if (string.IsNullOrWhiteSpace(defaultValue) == false)
                            {
                                if (Path.IsPathRooted(defaultValue) == false)
                                {
                                    set.Arguments[param.Name] = Path.Combine(Path.GetDirectoryName(fileName)!, defaultValue);
                                }
                            }
                        }
                    }
                }
            }

            return scriptConfig.Actions;
        }
        catch
        {
            return Enumerable.Empty<ScriptConfig>();
        }
    }
}