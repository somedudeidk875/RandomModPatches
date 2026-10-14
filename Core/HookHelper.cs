using System;
using System.Reflection;

namespace SOTSPerfPatch.Core;

//apparently this was needed and i found out why so many mods have something like this a bit late lol
internal static class HookHelper
{
	private const BindingFlags AnyDeclared =
		BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

	public static MethodInfo Method(Type type, string name, params Type[] parameters)
	{
		MethodInfo method = type.GetMethod(name, AnyDeclared, binder: null, types: parameters, modifiers: null);
		if (method != null)
			return method;

		var names = new string[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
			names[i] = parameters[i].Name;

		string signature = string.Join(", ", names);
		throw new MissingMethodException(
			$"Hook target {type.FullName}.{name}({signature}) was not found. SOTS may have been updated.");
	}

	public static Type ModType(string modName, string fullTypeName)
	{
		if (!Terraria.ModLoader.ModLoader.TryGetMod(modName, out var mod))
			throw new TypeLoadException($"{modName} is not loaded.");

		Type type = mod.GetType().Assembly.GetType(fullTypeName);
		if (type != null)
			return type;

		throw new TypeLoadException($"Type {fullTypeName} was not found in {modName}. It may have been updated.");
	}
}
