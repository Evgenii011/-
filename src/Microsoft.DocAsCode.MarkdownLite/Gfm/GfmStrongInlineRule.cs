// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.MarkdownLite
{
    using System.Text.RegularExpressions;

    public class GfmStrongInlineRule : MarkdownStrongInlineRule
    {
        public override string Name => "Inline.Gfm.Strong";

        public override Regex Strong => Regexes.Inline.Gfm.Strong;
    }
}
