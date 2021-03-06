// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
namespace Microsoft.DocAsCode.MarkdigEngine.Extensions
{
    using Markdig.Helpers;
    using Markdig.Parsers;
    using Markdig.Renderers.Html;
    using Markdig.Syntax;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
 
    public class TripleColonParser : BlockParser
    {
        private static readonly IDictionary<string, string> EmptyAttributes = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        private readonly MarkdownContext _context;
        private readonly IDictionary<string, ITripleColonExtensionInfo> _extensions;

        public TripleColonParser(MarkdownContext context, IDictionary<string, ITripleColonExtensionInfo> extensions)
        {
            OpeningCharacters = new[] { ':' };
            _context = context;
            _extensions = extensions;
        }
        
        public override BlockState TryOpen(BlockProcessor processor)
        {
            var slice = processor.Line;
            var column = processor.Column;
            var sourcePosition = processor.Start;

            if (processor.IsCodeIndent
                || !ExtensionsHelper.MatchStart(ref slice, ":::"))
            {
                return BlockState.None;
            }

            ExtensionsHelper.SkipSpaces(ref slice);

            var extensionName = "triple-colon";
            ITripleColonExtensionInfo extension;
            IDictionary<string, string> attributes;
            HtmlAttributes htmlAttributes;
            IDictionary<string, string> renderProperties;
            Action<string> logError = (string message) => _context.LogError($"invalid-{extensionName}", $"Invalid {extensionName} on line {processor.LineIndex}. \"{slice.Text}\" is invalid. {message}", line: processor.LineIndex);
            if (!TryMatchIdentifier(ref slice, out extensionName)
                || !_extensions.TryGetValue(extensionName, out extension)
                || !extension.TryValidateAncestry(processor.CurrentContainer, logError)
                || !TryMatchAttributes(ref slice, out attributes, extensionName, extension.SelfClosing, logError)
                || !extension.TryProcessAttributes(attributes, out htmlAttributes, out renderProperties, logError))
            {
                return BlockState.None;
            }

            var block = new TripleColonBlock(this)
            {
                Column = column,
                Span = new SourceSpan(sourcePosition, slice.End),
                Extension = extension,
                RenderProperties = renderProperties
            };

            if (htmlAttributes != null)
            {
                block.SetData(typeof(HtmlAttributes), htmlAttributes);
            }

            processor.NewBlocks.Push(block);

            if (extension.SelfClosing)
            {
                ExtensionsHelper.SkipSpaces(ref slice);
                if (!ExtensionsHelper.MatchStart(ref slice, ":::"))
                {
                    _context.LogWarning($"invalid-{extensionName}", $"Invalid {extensionName} on line {block.Line}. \"{slice.Text}\" is invalid. Blocks should be explicitly closed with :::");
                }

                return BlockState.BreakDiscard;
            }

            return BlockState.ContinueDiscard;
        }

        public override BlockState TryContinue(BlockProcessor processor, Block block)
        {
            var slice = processor.Line;
            if (processor.IsBlankLine)
            {
                return BlockState.Continue;
            }

            ExtensionsHelper.SkipSpaces(ref slice);

            if (!ExtensionsHelper.MatchStart(ref slice, ":::"))
            {
                ExtensionsHelper.ResetLineIndent(processor);
                return BlockState.Continue;
            }

            ExtensionsHelper.SkipSpaces(ref slice);

            var extensionName = ((TripleColonBlock)block).Extension.Name;

            if (!ExtensionsHelper.MatchStart(ref slice, extensionName) || !ExtensionsHelper.MatchStart(ref slice, "-end"))
            {
                ExtensionsHelper.ResetLineIndent(processor);
                return BlockState.Continue;
            }

            var c = ExtensionsHelper.SkipSpaces(ref slice);

            if (!c.IsZero())
            {
                _context.LogWarning($"invalid-{extensionName}", $"Invalid {extensionName} on line {block.Line}. \"{slice.Text}\" is invalid. Invalid character after \"::: {extensionName}-end\": \"{c}\"");
            }

            block.UpdateSpanEnd(slice.End);
            block.IsOpen = false;

            return BlockState.BreakDiscard;
        }

        public override bool Close(BlockProcessor processor, Block block)
        {
            var tripleColonBlock = (TripleColonBlock)block;
            if (tripleColonBlock.Extension.SelfClosing)
            {
                return true;
            }

            var extensionName = tripleColonBlock.Extension.Name;
            if (block.IsOpen)
            {
                _context.LogWarning($"invalid-{extensionName}", $"Invalid {extensionName} on line {block.Line}. No \"::: {extensionName}-end\" found. Blocks should be explicitly closed.");
            }
            return true;
        }

        private bool TryMatchIdentifier(ref StringSlice slice, out string name)
        {
            name = string.Empty;
            var c = slice.CurrentChar;
            if (c.IsAlpha())
            {
                var b = StringBuilderCache.Local();
                do
                {
                    b.Append(c);
                    c = slice.NextChar();
                } while (c.IsAlphaNumeric() || c == '-');
                name = b.ToString().ToLower();
                return true;
            }
            return false;
        }

        private bool TryMatchAttributeValue(ref StringSlice slice, out string value, string extensionName, string attributeName, Action<string> logError)
        {
            value = string.Empty;
            var c = slice.CurrentChar;
            if (c != '"')
            {
                logError($"Invalid attribute \"{attributeName}\". Values must be enclosed in double quotes.");
                return false;
            }
            var b = StringBuilderCache.Local();
            c = slice.NextChar();
            while (c != '"')
            {
                if (c.IsZero())
                {
                    logError($"Invalid attribute \"{attributeName}\". Values must be terminated with a double quote.");
                    return false;
                }
                b.Append(c);
                c = slice.NextChar();
            }
            slice.NextChar();
            value = b.ToString();
            return true;
        }

        private bool TryMatchAttributes(ref StringSlice slice, out IDictionary<string, string> attributes, string extensionName, bool selfClosing, Action<string> logError)
        {
            attributes = EmptyAttributes;
            while (true)
            {
                ExtensionsHelper.SkipSpaces(ref slice);
                if (slice.CurrentChar.IsZero() || (selfClosing && ExtensionsHelper.MatchStart(ref slice, ":::")))
                {
                    return true;
                }
                string attributeName;
                if (!TryMatchIdentifier(ref slice, out attributeName))
                {
                    logError($"Invalid attribute.");
                    return false;
                }
                if (attributes.ContainsKey(attributeName))
                {
                    logError($"Attribute \"{attributeName}\" specified multiple times.");
                    return false;
                }

                var value = string.Empty;

                ExtensionsHelper.SkipSpaces(ref slice);
                if (slice.CurrentChar == '=')
                {
                    slice.NextChar();
                    ExtensionsHelper.SkipSpaces(ref slice);
                    if (!TryMatchAttributeValue(ref slice, out value, extensionName, attributeName, logError))
                    {
                        return false;
                    }
                }

                if (attributes == EmptyAttributes)
                {
                    attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                attributes.Add(attributeName, value);
            }
        }
    }
}
