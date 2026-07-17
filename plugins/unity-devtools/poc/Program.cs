using System.Diagnostics;
using System.Globalization;
using Mono.Debugger.Soft;

namespace UnityDevtools.Poc;

/// <summary>
/// PoC CLI proving an external tool can attach to a running dev-Mono Unity game over the Mono Soft
/// Debugger protocol, inspect ECS state, and write a component back live.
/// Each subcommand is a full attach-act-detach cycle.
/// </summary>
internal static class Program {
  private const string DefaultHost = "127.0.0.1";

  // Options that take no value; every other --option consumes the next token.
  private static readonly string[] Flags = ["--members"];

  private static int Main(string[] args) {
    if (args.Length == 0) {
      return Program.Usage();
    }

    try {
      return args[0] switch {
        "status" => Commands.Status(Program.ArgValue(args, "--process") ?? "Cities2"),
        "attach-check" => Commands.AttachCheck(Program.Host(args), Program.RequirePort(args)),
        "find-types" => Commands.FindTypes(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "type full name"),
          args.Contains("--members")),
        "query" => Commands.Query(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positionals(args).Skip(1).ToArray(),
          int.Parse(Program.ArgValue(args, "--limit") ?? "10"),
          Program.ArgValue(args, "--world"),
          Program.ArgValue(args, "--label")),
        "call-static" => Commands.CallStatic(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "type full name"),
          Program.Positional(args, 2, "method name"),
          Program.Positionals(args).Skip(3).ToArray(),
          Program.ArgValue(args, "--world")),
        "call-system" => Commands.CallSystem(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "system type full name"),
          Program.Positional(args, 2, "method name"),
          Program.Positionals(args).Skip(3).ToArray(),
          Program.ArgValue(args, "--world")),
        "get-buffer" => Commands.GetBuffer(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "buffer element type full name"),
          Program.ArgValue(args, "--entity")
          ?? throw new ArgumentException("missing --entity <index[:version]>"),
          Program.ArgValue(args, "--world")),
        "buffer-add" => Commands.BufferAdd(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "buffer element type full name"),
          Program.ArgValue(args, "--entity")
          ?? throw new ArgumentException("missing --entity <index[:version]>"),
          Program.ArgValue(args, "--set") ??
          throw new ArgumentException("missing --set <field>=<value>"),
          Program.ArgValue(args, "--world")),
        "buffer-remove-at" => Commands.BufferRemoveAt(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "buffer element type full name"),
          Program.ArgValue(args, "--entity")
          ?? throw new ArgumentException("missing --entity <index[:version]>"),
          int.Parse(Program.ArgValue(args, "--index")
                    ?? throw new ArgumentException("missing --index <n>")),
          Program.ArgValue(args, "--world")),
        "get-component" => Commands.GetComponent(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "component full name"),
          Program.ArgValue(args, "--entity")
          ?? throw new ArgumentException("missing --entity <index[:version]>"),
          Program.ArgValue(args, "--world")),
        "set-component" => Commands.SetComponent(
          Program.Host(args),
          Program.RequirePort(args),
          Program.Positional(args, 1, "component full name"),
          Program.ArgValue(args, "--entity")
          ?? throw new ArgumentException("missing --entity <index[:version]>"),
          Program.ArgValue(args, "--field") ?? throw new ArgumentException("missing --field"),
          Program.ArgValue(args, "--value") ?? throw new ArgumentException("missing --value"),
          Program.ArgValue(args, "--world")),
        _ => Program.Usage()
      };
    }
    catch (Exception e) {
      Console.Error.WriteLine($"error: {e.Message}");
      return 1;
    }
  }

  private static int Usage() {
    Console.Error.WriteLine("""
      unity-devtools-poc <command> [options]

      commands:
        status [--process <name>]   find the game process and its SDB port (no attach)
        attach-check --port <port>  attach, print VM info, resume, detach
        find-types <full-name> [--members] --port <port>
                                    resolve a type by fully-qualified name (case-insensitive);
                                    --members lists its fields, properties, and methods
        query <comp> [<comp>...] [--limit N] [--label <sysType>:<method>] [--world <name>]
                      --port <port>
                                    count + list entities having ALL the given component types;
                                    --label annotates each via a system call, e.g.
                                    --label Game.UI.NameSystem:GetRenderedLabelName
        call-system <sysType> <method> [<arg>...] [--world <name>] --port <port>
                                    call a managed system method; args: idx:ver = Entity,
                                    ints/bools/floats as typed, anything else a string
        call-static <type> <method> [<arg>...] [--world <name>] --port <port>
                                    call a static method; extra arg tokens: em = the
                                    EntityManager, out-int / out-entity = out-param
                                    placeholders (out values are printed)
        get-buffer <elemType> --entity <index[:version]> [--world <name>] --port <port>
                                    print an entity's DynamicBuffer elements
        buffer-add <elemType> --entity <e> --set <field>=<value> [--world <name>] --port <port>
                                    append a buffer element (cloned from element 0, then --set)
        buffer-remove-at <elemType> --entity <e> --index <n> [--world <name>] --port <port>
                                    remove one buffer element by index
        get-component <comp> --entity <index[:version]> [--world <name>] --port <port>
                                    print one entity's component field values
        set-component <comp> --entity <index[:version]> --field <name> --value <v>
                      [--world <name>] --port <port>
                                    write one field of one entity's component, read it back

      common options:
        --host <ip>    debugger host (default 127.0.0.1)
        --port <port>  SDB port (see `status`)
      """);

    return 2;
  }

  private static string Host(string[] args) =>
    Program.ArgValue(args, "--host") ?? Program.DefaultHost;

  private static int RequirePort(string[] args) {
    var raw =
      Program.ArgValue(args, "--port") ??
      throw new ArgumentException("missing --port (run `status` to discover it)");

    return int.Parse(raw);
  }

  private static List<string> Positionals(string[] args) {
    var positionals = new List<string>();

    for (var i = 0; i < args.Length; i++) {
      if (args[i].StartsWith("--")) {
        if (!Program.Flags.Contains(args[i])) {
          i++;
        }

        continue;
      }

      positionals.Add(args[i]);
    }

    return positionals;
  }

  private static string Positional(string[] args, int index, string what) {
    var positionals = Program.Positionals(args);

    if (index >= positionals.Count) {
      throw new ArgumentException($"missing argument: {what}");
    }

    return positionals[index];
  }

  internal static string ArgValue(string[] args, string name) {
    var i = Array.IndexOf(args, name);

    if (i < 0) {
      return null;
    }

    if (i + 1 >= args.Length) {
      throw new ArgumentException($"{name} expects a value");
    }

    return args[i + 1];
  }
}

internal static class Commands {
  /// <summary>
  ///   Finds the game process and its Mono Soft Debugger listen port (56000-56511 range)
  ///   by parsing netstat, so nothing touches the port before a real attach.
  /// </summary>
  public static int Status(string processName) {
    var processes = Process.GetProcesses()
      .Where(p => p.ProcessName.StartsWith(processName, StringComparison.OrdinalIgnoreCase))
      .ToList();

    if (processes.Count == 0) {
      Console.WriteLine($"process '{processName}*': not running");

      return 1;
    }

    foreach (var p in processes) {
      Console.WriteLine($"process: {p.ProcessName} (pid {p.Id})");

      var ports = Commands.ListenPorts(p.Id);
      var sdb = ports.Where(port => port >= 56000 && port <= 56511).ToList();

      Console.WriteLine($"listening: {string.Join(", ", ports.OrderBy(x => x))}");

      Console.WriteLine(sdb.Count > 0
        ? $"sdb port: {sdb[0]}"
        : "sdb port: none found in 56000-56511 (not a dev/Mono build?)");
    }

    return 0;
  }

  /// <summary>Attaches, prints VM/runtime info, then resumes and detaches.</summary>
  public static int AttachCheck(string host, int port) {
    using var session = SdbSession.Connect(host, port);

    var vm = session.Vm;

    Console.WriteLine($"attached: {host}:{port}");
    Console.WriteLine($"vm version: {vm.Version.VMVersion}");
    Console.WriteLine($"protocol: {vm.Version.MajorVersion}.{vm.Version.MinorVersion}");

    var threads = vm.GetThreads();

    Console.WriteLine($"threads: {threads.Count}");

    foreach (var t in threads.Take(8)) {
      Console.WriteLine($"  [{t.Id}] {t.Name}");
    }

    Console.WriteLine("resuming + detaching...");

    return 0;
  }

  /// <summary>
  ///   Resolves a type by fully-qualified name over SDB and prints where it lives;
  ///   with --members, also lists its fields, properties, and methods.
  /// </summary>
  public static int FindTypes(string host, int port, string fullName, bool members) {
    using var session = SdbSession.Connect(host, port);

    var types = session.Vm.GetTypes(fullName, true);

    if (types.Count == 0) {
      Console.WriteLine(
        $"type '{fullName}': not found (name must be fully qualified; discover names offline, " +
        $"e.g. ilspycmd on Cities2_Data/Managed)"
      );

      return 1;
    }

    foreach (var t in types) {
      Console.WriteLine($"{t.FullName}");
      Console.WriteLine($"  assembly: {t.Assembly.GetName().Name}");
      Console.WriteLine(
        $"  kind: {(t.IsValueType ? "struct" : t.IsInterface ? "interface" : "class")}"
      );

      if (!members) {
        continue;
      }

      Console.WriteLine("  fields:");

      foreach (var f in t.GetFields().Where(f => !f.IsStatic)) {
        Console.WriteLine($"    {f.Name}: {f.FieldType.FullName}");
      }

      Console.WriteLine("  properties:");

      foreach (var p in t.GetProperties()) {
        Console.WriteLine($"    {p.Name}: {p.PropertyType.FullName}");
      }

      Console.WriteLine("  methods:");

      foreach (var m in t.GetMethods()) {
        var pars = string.Join(", ", m.GetParameters()
          .Select(x => $"{x.ParameterType.Name} {x.Name}"));

        Console.WriteLine($"    {m.ReturnType.Name} {m.Name}({pars})");
      }
    }

    return 0;
  }

  /// <summary>
  ///   Counts and lists entities having all the given component types; with --label, each
  ///   listed entity is annotated via a one-Entity-arg system method (e.g. a name system).
  /// </summary>
  public static int Query(
    string host, int port, string[] comps, int limit, string world, string label
  ) {
    if (comps.Length == 0) {
      throw new ArgumentException("missing component type name(s)");
    }

    using var session = SdbSession.Connect(host, port);
    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);
    var types = comps.Select(inv.ResolveType).ToArray();
    var query = ecs.CreateQuery(types);
    var count = ecs.Count(query);

    Console.WriteLine($"entities matching [{string.Join(", ", comps)}]: {count}");

    Value labelSystem = null;
    MethodMirror labelMethod = null;

    if (label != null) {
      var parts = label.Split(':');

      if (parts.Length != 2) {
        throw new ArgumentException("--label expects <systemTypeFullName>:<method>");
      }

      labelSystem = ecs.GetSystem(parts[0]);
      labelMethod = inv.FindMethod(inv.TypeOf(labelSystem), parts[1], 1);
    }

    if (count > 0 && limit > 0) {
      var arr = ecs.EntityArray(query);
      var take = Math.Min(limit, arr.Length);

      foreach (var e in arr.GetValues(0, take)) {
        var annotation = labelSystem != null
          ? $"  {inv.Format(inv.Invoke(labelSystem, labelMethod, e))}"
          : "";

        Console.WriteLine($"  {inv.Format(e)}{annotation}");
      }

      if (arr.Length > take) {
        Console.WriteLine($"  ... ({arr.Length - take} more; raise --limit to see them)");
      }
    }

    inv.Invoke(query, "Dispose");

    return 0;
  }

  /// <summary>Calls a managed system method with loosely typed CLI arguments.</summary>
  public static int CallSystem(
    string host, int port, string sysType, string method, string[] rawArgs, string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var system = ecs.GetSystem(sysType);
    var args = rawArgs.Select(a => Commands.ParseCallArg(ecs, inv, a)).ToArray();
    var m = inv.FindMethod(inv.TypeOf(system), method, args.Length);

    Console.WriteLine(inv.Format(inv.Invoke(system, m, args), 3));

    return 0;
  }

  /// <summary>Prints an entity's DynamicBuffer elements.</summary>
  public static int GetBuffer(
    string host, int port, string elem, string entitySpec, string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var (index, version) = Commands.ParseEntitySpec(entitySpec);
    var buf = ecs.GetBuffer(ecs.MakeEntity(index, version ?? 1), inv.ResolveType(elem));
    var len = (int)((PrimitiveValue)inv.GetProperty(buf, "Length")).Value;

    Console.WriteLine($"{elem}[{len}] on entity {index}:{version ?? 1}");

    for (var i = 0; i < len; i++) {
      Console.WriteLine($"  [{i}] {inv.Format(inv.Invoke(buf, "get_Item", inv.Prim(i)), 3)}");
    }

    return 0;
  }

  /// <summary>
  ///   Appends a buffer element: clones element 0 as a template, applies --set, adds it.
  /// </summary>
  public static int BufferAdd(
    string host, int port, string elem, string entitySpec, string setSpec, string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var elemType = inv.ResolveType(elem);
    var (index, version) = Commands.ParseEntitySpec(entitySpec);
    var buf = ecs.GetBuffer(ecs.MakeEntity(index, version ?? 1), elemType);
    var len = (int)((PrimitiveValue)inv.GetProperty(buf, "Length")).Value;

    if (len == 0) {
      throw new InvalidOperationException(
        "buffer is empty; PoC clones element 0 as the template for new elements"
      );
    }

    var element = (StructMirror)inv.Invoke(buf, "get_Item", inv.Prim(0));
    var eq = setSpec.IndexOf('=');

    if (eq <= 0) {
      throw new ArgumentException("--set expects <field>=<value>");
    }

    var fieldName = setSpec[..eq];

    var fieldInfo =
      Invoker.InstanceFields(elemType)
        .FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
      ?? throw new ArgumentException(
        $"field '{fieldName}' not found on {elemType.FullName}");

    element[fieldInfo.Name] = ecs.ParseFieldValue(fieldInfo.FieldType, setSpec[(eq + 1)..]);
    inv.Invoke(buf, "Add", element);

    Console.WriteLine($"added: {inv.Format(element, 3)}");
    Console.WriteLine($"new length: {inv.Format(inv.GetProperty(buf, "Length"))}");

    return 0;
  }

  /// <summary>Removes one buffer element by index.</summary>
  public static int BufferRemoveAt(
    string host, int port, string elem, string entitySpec, int at, string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var (index, version) = Commands.ParseEntitySpec(entitySpec);
    var buf = ecs.GetBuffer(ecs.MakeEntity(index, version ?? 1), inv.ResolveType(elem));
    var removed = inv.Invoke(buf, "get_Item", inv.Prim(at));

    Console.WriteLine($"removing [{at}]: {inv.Format(removed, 3)}");

    inv.Invoke(buf, "RemoveAt", inv.Prim(at));

    Console.WriteLine($"new length: {inv.Format(inv.GetProperty(buf, "Length"))}");

    return 0;
  }

  /// <summary>Calls a static method; out-param values are printed after the result.</summary>
  public static int CallStatic(
    string host, int port, string typeName, string method, string[] rawArgs, string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var type = inv.ResolveType(typeName);
    var args = rawArgs.Select(a => Commands.ParseCallArg(ecs, inv, a)).ToArray();
    var m = inv.FindMethod(type, method, args.Length);
    var result = inv.InvokeStaticWithOutArgs(type, m, args);

    Console.WriteLine($"result: {inv.Format(result.Result, 3)}");

    if (result.OutArgs == null) {
      return 0;
    }

    for (var i = 0; i < result.OutArgs.Length; i++) {
      Console.WriteLine($"arg[{i}] after call: {inv.Format(result.OutArgs[i], 3)}");
    }

    return 0;
  }

  private static Value ParseCallArg(Ecs ecs, Invoker inv, string raw) {
    switch (raw) {
      case "em":
        return ecs.EntityManager;
      case "out-int":
        return inv.Prim(0);
      case "out-entity":
        return ecs.MakeEntity(0, 0);
    }

    var parts = raw.Split(':');

    if (
      parts.Length == 2 &&
      int.TryParse(parts[0], out var ei) &&
      int.TryParse(parts[1], out var ev)
    ) {
      return ecs.MakeEntity(ei, ev);
    }

    if (int.TryParse(raw, out var i)) {
      return inv.Prim(i);
    }

    if (bool.TryParse(raw, out var b)) {
      return inv.Prim(b);
    }

    if (float.TryParse(raw, CultureInfo.InvariantCulture, out var f)) {
      return inv.Prim(f);
    }

    return inv.Str(raw);
  }

  /// <summary>Prints one entity's component field values.</summary>
  public static int GetComponent(
    string host, int port, string comp, string entitySpec, string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var compType = inv.ResolveType(comp);
    var query = ecs.CreateQuery([compType]);
    var (index, version) = Commands.ParseEntitySpec(entitySpec);
    var entity = ecs.FindEntity(query, index, version);

    Console.WriteLine($"entity: {inv.Format(entity)}");
    Console.WriteLine($"{compType.FullName}: {inv.Format(ecs.GetComponent(entity, compType), 3)}");

    inv.Invoke(query, "Dispose");

    return 0;
  }

  /// <summary>Writes one field of one entity's component, then reads it back.</summary>
  public static int SetComponent(
    string host,
    int port,
    string comp,
    string entitySpec,
    string field,
    string rawValue,
    string world
  ) {
    using var session = SdbSession.Connect(host, port);

    var inv = new Invoker(session.Vm);
    var ecs = new Ecs(inv, world);

    var compType = inv.ResolveType(comp);
    var query = ecs.CreateQuery([compType]);
    var (index, version) = Commands.ParseEntitySpec(entitySpec);
    var entity = ecs.FindEntity(query, index, version);

    var fieldInfo =
      Invoker.InstanceFields(compType)
        .FirstOrDefault(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase))
      ?? throw new ArgumentException(
        $"field '{field}' not found on {compType.FullName}; "
        + $"fields: {string.Join(", ", Invoker.InstanceFields(compType).Select(f => f.Name))}"
      );

    var value = (StructMirror)ecs.GetComponent(entity, compType);

    Console.WriteLine($"entity: {inv.Format(entity)}");
    Console.WriteLine($"before: {inv.Format(value, 3)}");

    value[fieldInfo.Name] = ecs.ParseFieldValue(fieldInfo.FieldType, rawValue);
    ecs.SetComponent(entity, compType, value);

    Console.WriteLine($"after (read back): {inv.Format(ecs.GetComponent(entity, compType), 3)}");

    inv.Invoke(query, "Dispose");

    return 0;
  }

  private static (int index, int? version) ParseEntitySpec(string spec) {
    var parts = spec.Split(':');

    return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : null);
  }

  private static List<int> ListenPorts(int pid) {
    // netstat -ano is the lightest way to map listen ports to a PID on Windows without P/Invoke;
    // fine for a PoC.
    var psi = new ProcessStartInfo("netstat", "-ano -p tcp") {
      RedirectStandardOutput = true,
      UseShellExecute = false
    };

    using var netstat = Process.Start(psi);

    var output = netstat!.StandardOutput.ReadToEnd();

    netstat.WaitForExit();

    var ports = new List<int>();

    foreach (var line in output.Split('\n')) {
      var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

      // TCP <local> <remote> LISTENING <pid>
      if (
        cols.Length >= 5 &&
        cols[0] == "TCP" &&
        cols[3] == "LISTENING" &&
        cols[4].Trim() == pid.ToString()
      ) {
        var local = cols[1];
        var idx = local.LastIndexOf(':');

        if (idx > 0 && int.TryParse(local[(idx + 1)..], out var port)) {
          ports.Add(port);
        }
      }
    }

    return ports.Distinct().ToList();
  }
}
