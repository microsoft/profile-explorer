// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows.Media;
using ProfileExplorerCore.Settings;
using ProfileExplorerUI.Settings;

namespace TestTypeRegistration
{
    class Program
    {
        static void Main()
        {
            // Register the Color converter
            SettingsTypeRegistry.RegisterConverter(new ColorSettingsConverter());
            
            // Test the conversion
            var converter = SettingsTypeRegistry.GetConverter(typeof(Color));
            
            if (converter != null)
            {
                var color = converter.ConvertFromString("#FF0000");
                Console.WriteLine($"Successfully converted '#FF0000' to Color: {color}");
                
                var colors = converter.ConvertFromStringArray(new[] { "#FF0000", "#00FF00" });
                Console.WriteLine($"Successfully converted array to {colors.Length} colors");
                
                Console.WriteLine("Type registration system is working correctly!");
            }
            else
            {
                Console.WriteLine("Failed to get converter for Color type");
            }
        }
    }
}
