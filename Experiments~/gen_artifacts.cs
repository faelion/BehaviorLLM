// Generates the prompt + schema artifacts for the experiment matrix using the
// package's own builders, so the harness measures exactly what ships.
// Runs through `unity command eval_file`, which wraps this in a method body:
// no using directives, everything fully qualified.

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

var reactiveOpts = new BehaviorLLM.Core.Decisions.PromptBuilder.SystemPromptOptions();
reactiveOpts.Persona = persona;
reactiveOpts.IncludeExamples = true;
reactiveOpts.ReasonMaxChars = 0;

var delibOpts = new BehaviorLLM.Core.Decisions.PromptBuilder.SystemPromptOptions();
delibOpts.Persona = persona;
delibOpts.IncludeExamples = true;
delibOpts.ReasonMaxChars = 120;

string reactiveSystem = BehaviorLLM.Core.Decisions.PromptBuilder.BuildSystemPrompt(config, reactiveOpts);
string deliberativeSystem = BehaviorLLM.Core.Decisions.PromptBuilder.BuildSystemPrompt(config, delibOpts);

var schemaReactiveOpts = new BehaviorLLM.Core.Backend.ActionSchemaBuilder.Options();
schemaReactiveOpts.ReasonMaxChars = 0;
var schemaDelibOpts = new BehaviorLLM.Core.Backend.ActionSchemaBuilder.Options();
schemaDelibOpts.ReasonMaxChars = 120;

string reactiveSchema = BehaviorLLM.Core.Backend.ActionSchemaBuilder.Build(config, schemaReactiveOpts);
string deliberativeSchema = BehaviorLLM.Core.Backend.ActionSchemaBuilder.Build(config, schemaDelibOpts);

string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs/BehaviorLLMExperiments"));
System.IO.Directory.CreateDirectory(dir);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "system_reactive.txt"), reactiveSystem, System.Text.Encoding.UTF8);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "system_deliberative.txt"), deliberativeSystem, System.Text.Encoding.UTF8);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "schema_reactive.json"), reactiveSchema, System.Text.Encoding.UTF8);
System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "schema_deliberative.json"), deliberativeSchema, System.Text.Encoding.UTF8);

UnityEngine.Object.DestroyImmediate(config);
return "reactiveSystem=" + reactiveSystem.Length + " deliberativeSystem=" + deliberativeSystem.Length
     + " reactiveSchema=" + reactiveSchema.Length + " deliberativeSchema=" + deliberativeSchema.Length;
