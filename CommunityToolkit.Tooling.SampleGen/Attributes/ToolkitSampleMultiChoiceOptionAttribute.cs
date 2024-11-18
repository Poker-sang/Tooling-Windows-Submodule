// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace CommunityToolkit.Tooling.SampleGen.Attributes;

/// <summary>
/// Generates a property and multi-choice option in the sample option pane that can be used to update it.
/// </summary>
/// <remarks>
/// Using this attribute will automatically generate an <see cref="INotifyPropertyChanged"/>-enabled property
/// that you can bind to in XAML, and displays an options pane alongside your sample which allows the user to manipulate the property.
/// <para/>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ToolkitSampleMultiChoiceOptionAttribute : ToolkitSampleOptionBaseAttribute
{
    /// <summary>
    /// Creates a new instance of <see cref="ToolkitSampleMultiChoiceOptionAttribute"/>.
    /// </summary>
    /// <param name="bindingName">The name of the generated property, which you can bind to in XAML.</param>
    /// <param name="choices">A list of the choices to display to the user. Can be literal values, or labeled values. Use a " : " separator (single colon surrounded by at least 1 whitespace) to separate a label from a value.</param>
    public ToolkitSampleMultiChoiceOptionAttribute(string bindingName, params string[] choices)
        : base(bindingName, null)
    {
        TypeName = "string";
        Choices = choices.Select(x =>
        {
            if (x.Contains(" : "))
            {
                var parts = x.Split(new string[] { " : " }, StringSplitOptions.RemoveEmptyEntries);
                return new MultiChoiceOption(parts[0].TrimEnd(), parts[1].TrimStart());
            }

            return new MultiChoiceOption(x, x);
        }).ToArray();
    }

    /// <summary>
    /// Creates a new instance of <see cref="ToolkitSampleMultiChoiceOptionAttribute"/>.
    /// </summary>
    /// <param name="bindingName">The name of the generated property, which you can bind to in XAML.</param>
    /// <param name="choices">A list of the choices to display to the user. Can be literal values, or labeled values. Use a " : " separator (single colon surrounded by at least 1 whitespace) to separate a label from a value.</param>
    public ToolkitSampleMultiChoiceOptionAttribute(string bindingName, List<(string, object)> choices)
        : base(bindingName, null)
    {
        TypeName = "int";
        Choices = choices.Select(x => new MultiChoiceOption(x.Item1, x.Item2)).ToArray();
    }

    /// <summary>
    /// A collection of choices to display in the options pane.
    /// </summary>
    public MultiChoiceOption[] Choices { get; }

    /// <summary>
    /// The source generator-friendly type name used for casting.
    /// </summary>
    internal override string TypeName { get; }
}
