﻿using System;
using System.IO;
using System.Text.Json;
using ScriptRunner.GUI.ScriptConfigs;

namespace ScriptRunner.GUI.ScriptReader;

public static class ScriptConfigReader
{
    public static ActionsConfig Load()
    {
        var fileName = Path.Combine(AppContext.BaseDirectory,"Scripts/TextInputScript.json");
        var jsonString = File.ReadAllText(fileName);
        var scriptConfig = JsonSerializer.Deserialize<ActionsConfig>(jsonString, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new PromptTypeJsonConverter(), new ParamTypeJsonConverter() }
        })!;


        foreach (var action in scriptConfig.Actions)
        {
            var defaultSet = new ArgumentSet()
            {
                Description = "<default>"
            };
            foreach (var param in action.Params)
            {
                defaultSet.Arguments[param.Name] = param.Default;
            }
            action.PredefinedArgumentSets.Insert(0, defaultSet);
        }

        return scriptConfig;
    }
}