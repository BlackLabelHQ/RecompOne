using System.Reflection;
using MonoMod.RuntimeDetour;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Modding;

public static class HookManager
{
    private sealed class Entry<T>
    {
        public ModInfo Mod = null!;
        public T Fn = default!;
    }

    private sealed class FunctionHooks
    {
        public Entry<Func<CpuContext, IMemory, bool>>[] Pres = [];
        public Entry<Action<CpuContext, IMemory>>[] Posts = [];
        public Action<Action<CpuContext, IMemory>, CpuContext, IMemory>? Replace;
        public ModInfo? ReplaceOwner;
        public Hook? Hook;

        public bool Empty => Pres.Length == 0 && Posts.Length == 0 && Replace == null;
    }

    private static readonly Dictionary<MethodInfo, FunctionHooks> _hooks = [];
    private static readonly object _gate = new();

    private static readonly Type[] SigBasic = [typeof(CpuContext), typeof(IMemory)];
    private static readonly Type[] SigOrig = [typeof(Action<CpuContext, IMemory>), typeof(CpuContext), typeof(IMemory)];

    public static int HookedFunctionCount
    {
        get
        {
            lock (_gate)
            {
                return _hooks.Count;
            }
        }
    }

    public static int CountForMod(ModInfo mod)
    {
        lock (_gate)
        {
            var n = 0;
            foreach (var (_, h) in _hooks)
            {
                foreach (var p in h.Pres)
                    if (p.Mod == mod)
                        n++;
                foreach (var p in h.Posts)
                    if (p.Mod == mod)
                        n++;
                if (h.ReplaceOwner == mod) n++;
            }

            return n;
        }
    }

    public static bool AddReplace(ModInfo mod, MethodInfo target, MethodInfo impl)
    {
        Action<Action<CpuContext, IMemory>, CpuContext, IMemory> replace;
        if (Matches(impl, typeof(void), SigOrig))
        {
            replace = impl.CreateDelegate<Action<Action<CpuContext, IMemory>, CpuContext, IMemory>>();
        }
        else if (Matches(impl, typeof(void), SigBasic))
        {
            var direct = impl.CreateDelegate<Action<CpuContext, IMemory>>();
            replace = (orig, c, m) => direct(c, m);
        }
        else
        {
            Console.Error.WriteLine($"[Mods] {mod.Id}: invalid replace signature on {Describe(impl)}");
            return false;
        }

        lock (_gate)
        {
            var hooks = Get(target);
            if (hooks.ReplaceOwner != null)
            {
                Console.Error.WriteLine(
                    $"[Mods] replace conflict on {target.Name}: {mod.Id} ignored, {hooks.ReplaceOwner.Id} already owns it");
                return false;
            }

            hooks.Replace = replace;
            hooks.ReplaceOwner = mod;
        }

        return true;
    }

    public static bool AddPre(ModInfo mod, MethodInfo target, MethodInfo impl)
    {
        Func<CpuContext, IMemory, bool> pre;
        if (Matches(impl, typeof(bool), SigBasic))
        {
            pre = impl.CreateDelegate<Func<CpuContext, IMemory, bool>>();
        }
        else if (Matches(impl, typeof(void), SigBasic))
        {
            var direct = impl.CreateDelegate<Action<CpuContext, IMemory>>();
            pre = (c, m) =>
            {
                direct(c, m);
                return true;
            };
        }
        else
        {
            Console.Error.WriteLine($"[Mods] {mod.Id}: invalid pre hook signature on {Describe(impl)}");
            return false;
        }

        lock (_gate)
        {
            var hooks = Get(target);
            hooks.Pres = [.. hooks.Pres, new Entry<Func<CpuContext, IMemory, bool>> { Mod = mod, Fn = pre }];
        }

        return true;
    }

    public static bool AddPost(ModInfo mod, MethodInfo target, MethodInfo impl)
    {
        if (!Matches(impl, typeof(void), SigBasic))
        {
            Console.Error.WriteLine($"[Mods] {mod.Id}: invalid post hook signature on {Describe(impl)}");
            return false;
        }

        var post = impl.CreateDelegate<Action<CpuContext, IMemory>>();
        lock (_gate)
        {
            var hooks = Get(target);
            hooks.Posts = [.. hooks.Posts, new Entry<Action<CpuContext, IMemory>> { Mod = mod, Fn = post }];
        }

        return true;
    }

    public static void RemoveMod(ModInfo mod)
    {
        lock (_gate)
        {
            foreach (var target in _hooks.Keys.ToArray())
            {
                var hooks = _hooks[target];
                hooks.Pres = hooks.Pres.Where(p => p.Mod != mod).ToArray();
                hooks.Posts = hooks.Posts.Where(p => p.Mod != mod).ToArray();
                if (hooks.ReplaceOwner == mod)
                {
                    hooks.Replace = null;
                    hooks.ReplaceOwner = null;
                }

                if (hooks.Empty)
                {
                    hooks.Hook?.Dispose();
                    hooks.Hook = null;
                    _hooks.Remove(target);
                }
            }
        }
    }

    public static void Commit()
    {
        lock (_gate)
        {
            foreach (var (target, hooks) in _hooks)
            {
                if (hooks.Hook != null) continue;
                var state = hooks;
                hooks.Hook = new Hook(target,
                    (Action<Action<CpuContext, IMemory>, CpuContext, IMemory>)
                    ((orig, c, m) => Invoke(state, orig, c, m)));
            }
        }
    }

    private static void Invoke(FunctionHooks hooks, Action<CpuContext, IMemory> orig, CpuContext c, IMemory m)
    {
        var pres = hooks.Pres;
        var posts = hooks.Posts;
        var replace = hooks.Replace;

        var skip = false;
        for (var i = 0; i < pres.Length; i++)
            if (!pres[i].Fn(c, m))
                skip = true;
        if (!skip)
        {
            if (replace != null) replace(orig, c, m);
            else orig(c, m);
        }

        for (var i = 0; i < posts.Length; i++)
            posts[i].Fn(c, m);
    }

    private static FunctionHooks Get(MethodInfo target)
    {
        if (!_hooks.TryGetValue(target, out var hooks))
            _hooks[target] = hooks = new FunctionHooks();
        return hooks;
    }

    private static bool Matches(MethodInfo impl, Type ret, Type[] args)
    {
        if (impl.ReturnType != ret) return false;
        var pars = impl.GetParameters();
        if (pars.Length != args.Length) return false;
        for (var i = 0; i < pars.Length; i++)
            if (pars[i].ParameterType != args[i])
                return false;
        return true;
    }

    private static string Describe(MethodInfo impl)
    {
        return $"{impl.DeclaringType?.Name}.{impl.Name}";
    }
}