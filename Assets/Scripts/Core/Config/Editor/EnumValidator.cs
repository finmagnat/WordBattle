#if UNITY_EDITOR

using System;
using System.Linq;
using Core.Data;
using UnityEditor;
using UnityEngine;

public static class EnumValidator
{
    [MenuItem("Tools/Validation/Validate Enums")]
    private static void ValidateEnums()
    {
        ValidateEnum<SkinSpriteKey>();
        ValidateEnum<SkinPrefabKey>();
        ValidateEnum<SkinColorKey>();

        Debug.Log("Enum validation completed.");
    }

    private static void ValidateEnum<T>() where T : Enum
    {
        var values = Enum.GetNames(typeof(T))
            .Select(name => new
            {
                Name = name,
                Value = Convert.ToInt64(
                    Enum.Parse(typeof(T), name))
            });

        var duplicates = values
            .GroupBy(x => x.Value)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicates)
        {
            Debug.LogError(
                $"Duplicate enum value in {typeof(T).Name}: " +
                $"{group.Key} = " +
                string.Join(", ", group.Select(x => x.Name)));
        }
    }
}

#endif