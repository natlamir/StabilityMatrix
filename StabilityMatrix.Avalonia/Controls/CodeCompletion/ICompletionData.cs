﻿// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using FluentAvalonia.UI.Controls;
using StabilityMatrix.Avalonia.Models;

namespace StabilityMatrix.Avalonia.Controls.CodeCompletion;

/// <summary>
/// Describes an entry in the <see cref="AvaloniaEdit.CodeCompletion.CompletionList"/>.
/// </summary>
/// <remarks>
/// Note that the CompletionList uses data binding against the properties in this interface.
/// Thus, your implementation of the interface must use public properties; not explicit interface implementation.
/// </remarks>
public interface ICompletionData
{
    /// <summary>
    /// Gets the text. This property is used to filter the list of visible elements.
    /// </summary>
    string Text { get; }

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the image.
    /// </summary>
    ImageSource? ImageSource { get; }

    /// <summary>
    /// Title of the image.
    /// </summary>
    string? ImageTitle { get; }

    /// <summary>
    /// Subtitle of the image.
    /// </summary>
    string? ImageSubtitle { get; }

    /// <summary>
    /// Whether the image is available.
    /// </summary>
    [MemberNotNullWhen(true, nameof(ImageSource))]
    bool HasImage => ImageSource != null;

    /// <summary>
    /// Gets the icon shown on the left.
    /// </summary>
    IconData? Icon { get; }

    /// <summary>
    /// Gets inline text fragments.
    /// </summary>
    InlineCollection TextInlines { get; }

    /// <summary>
    /// Gets the priority. This property is used in the selection logic. You can use it to prefer selecting those items
    /// which the user is accessing most frequently.
    /// </summary>
    double Priority { get; }

    /// <summary>
    /// Perform the completion.
    /// </summary>
    /// <param name="textArea">The text area on which completion is performed.</param>
    /// <param name="completionSegment">The text segment that was used by the completion window if
    /// the user types (segment between CompletionWindow.StartOffset and CompletionWindow.EndOffset).</param>
    /// <param name="eventArgs">The EventArgs used for the insertion request.
    /// These can be TextCompositionEventArgs, KeyEventArgs, MouseEventArgs, depending on how
    /// the insertion was triggered.</param>
    /// <param name="prepareText">Optional function to transform the text to be inserted</param>
    void Complete(
        TextArea textArea,
        ISegment completionSegment,
        InsertionRequestEventArgs eventArgs,
        Func<ICompletionData, string>? prepareText = null
    );

    /// <summary>
    /// Update the text character highlighting
    /// </summary>
    void UpdateCharHighlighting(string searchText);

    /// <summary>
    /// Reset the text character highlighting
    /// </summary>
    void ResetCharHighlighting();
}
