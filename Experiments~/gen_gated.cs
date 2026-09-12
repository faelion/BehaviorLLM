// Builds the schema the package produces when an IActionAvailabilityProvider removes
// Chase (health below half) and HoldPosition (nothing is happening), to test whether
// gating the menu fixes the conditional-rule failures the matrix found.

var config = UnityEngine.ScriptableObject.CreateInstance<BehaviorLLM.Core.Actions.ActionConfig>();
config.modelInstructions =
    "Chase an intruder the moment you can see one. If no intruder is visible but a disturbance was reported, investigate it. "
  + "If you are damaged below half health and an intruder is visible, retreat to a safe zone instead of chasing. "
  + "Otherwise keep patrolling your route.";

System.Func<string, string, BehaviorLLM.Core.Actions.ActionParameterType, string, string[], BehaviorLLM.Core.Actions.ActionDefinition> Def =
    (name, desc, t, example, allowed) =>
{
    var d = new BehaviorLLM.Core.Actions.ActionDefinition { actionName = name, description = desc, parameterType = t, exampleArgument = example };
    d.allowedArguments.AddRange(allowed);
    return d;
};
var none = BehaviorLLM.Core.Actions.ActionParameterType.None;
var str = BehaviorLLM.Core.Actions.ActionParameterType.String;

config.validActions.Add(Def("HoldPosition", "Stand still and keep watching.", none, "", new string[0]));
config.validActions.Add(Def("Patrol", "Walk a named patrol route.", str, "Route_North", new[] { "Route_North", "Route_South", "Route_Perimeter" }));
config.validActions.Add(Def("Investigate", "Go to a location where something was reported.", str, "Warehouse", new[] { "Warehouse", "Courtyard", "Gate" }));
config.validActions.Add(Def("Chase", "Pursue a visible hostile target.", str, "Intruder_01", new[] { "Intruder_01", "Intruder_02" }));
config.validActions.Add(Def("Retreat", "Fall back to a safe zone.", str, "SafeZone_A", new[] { "SafeZone_A", "SafeZone_B" }));

string persona = "You are Guard_01, a security guard patrolling a warehouse compound at night.";
var sysOpts = new BehaviorLLM.Core.Decisions.PromptBuilder.SystemPromptOptions();
sysOpts.Persona = persona;
sysOpts.IncludeExamples = true;
sysOpts.ReasonMaxChars = 0;
sysOpts.DynamicMenu = true;
string systemDynamic = BehaviorLLM.Core.Decisions.PromptBuilder.BuildSystemPrompt(config, sysOpts);

System.Func<string[], string> BuildFor = (available) =>
{
    var o = new BehaviorLLM.Core.Backend.ActionSchemaBuilder.Options();
    o.ReasonMaxChars = 0;
    o.IsActionAvailable = def => System.Array.IndexOf(available, def.actionName) >= 0;
    return BehaviorLLM.Core.Backend.ActionSchemaBuilder.Build(config, o);
};

// Hurt + intruder visible: the game knows chasing is wrong, so Chase is not offered.
string schemaHurt = BuildFor(new[] { "HoldPosition", "Patrol", "Investigate", "Retreat" });
// Nothing happening: holding position is not an interesting choice, so it is not offered.
string schemaQuiet = BuildFor(new[] { "Patrol", "Investigate", "Chase", "Retreat" });

string dir = @"C:\Users\alexx\AppData\Local\Temp\claude\D--repos-TFG-Assets-BehaviorLLM\73a39eeb-32fb-4be2-97a8-7f9c9752b255\scratchpad\matrix";
System.IO.Directory.CreateDirectory(dir);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "system_dynamic.txt"), systemDynamic, new System.Text.UTF8Encoding(false));
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "schema_gated_hurt.json"), schemaHurt, new System.Text.UTF8Encoding(false));
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "schema_gated_quiet.json"), schemaQuiet, new System.Text.UTF8Encoding(false));

UnityEngine.Object.DestroyImmediate(config);
return "systemDynamic=" + systemDynamic.Length + " hurtHasChase=" + schemaHurt.Contains("\"Chase\"") + " quietHasHold=" + schemaQuiet.Contains("\"HoldPosition\"");
