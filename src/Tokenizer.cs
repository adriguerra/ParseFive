#region Copyright (c) 2017 Atif Aziz, Adrian Guerra
//
// Portions Copyright (c) 2013 Ivan Nikulin
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
#endregion

namespace High5
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Extensions;
    using static NamedEntityData;
    using static NamedEntityTreeFlags;
    using String = Compatibility.String;
    using CP = Unicode.CodePoints;
    using CPS = Unicode.CodePointSequences;

    static class NamedEntityTreeFlags
    {
        public const int HasDataFlag = 1 << 0;
        public const int DataDupletFlag = 1 << 1;
        public const int HasBranchesFlag = 1 << 2;
        public const int MaxBranchMarkerValue = HasDataFlag | DataDupletFlag | HasBranchesFlag;
    }

    sealed class Tokenizer
    {
        // Replacement code points for numeric entities

        static readonly IDictionary<int, int> NumericEntityReplacements = new Dictionary<int, int>
        {
            { 0x00, 0xFFFD }, { 0x0D, 0x000D }, { 0x80, 0x20AC }, { 0x81, 0x0081 }, { 0x82, 0x201A }, { 0x83, 0x0192 }, { 0x84, 0x201E },
            { 0x85, 0x2026 }, { 0x86, 0x2020 }, { 0x87, 0x2021 }, { 0x88, 0x02C6 }, { 0x89, 0x2030 }, { 0x8A, 0x0160 }, { 0x8B, 0x2039 },
            { 0x8C, 0x0152 }, { 0x8D, 0x008D }, { 0x8E, 0x017D }, { 0x8F, 0x008F }, { 0x90, 0x0090 }, { 0x91, 0x2018 }, { 0x92, 0x2019 },
            { 0x93, 0x201C }, { 0x94, 0x201D }, { 0x95, 0x2022 }, { 0x96, 0x2013 }, { 0x97, 0x2014 }, { 0x98, 0x02DC }, { 0x99, 0x2122 },
            { 0x9A, 0x0161 }, { 0x9B, 0x203A }, { 0x9C, 0x0153 }, { 0x9D, 0x009D }, { 0x9E, 0x017E }, { 0x9F, 0x0178 }
        };

        // States

        const string DATA_STATE = "DATA_STATE";
        const string CHARACTER_REFERENCE_IN_DATA_STATE = "CHARACTER_REFERENCE_IN_DATA_STATE";
        const string RCDATA_STATE = "RCDATA_STATE";
        const string CHARACTER_REFERENCE_IN_RCDATA_STATE = "CHARACTER_REFERENCE_IN_RCDATA_STATE";
        const string RAWTEXT_STATE = "RAWTEXT_STATE";
        const string SCRIPT_DATA_STATE = "SCRIPT_DATA_STATE";
        const string PLAINTEXT_STATE = "PLAINTEXT_STATE";
        const string TAG_OPEN_STATE = "TAG_OPEN_STATE";
        const string END_TAG_OPEN_STATE = "END_TAG_OPEN_STATE";
        const string TAG_NAME_STATE = "TAG_NAME_STATE";
        const string RCDATA_LESS_THAN_SIGN_STATE = "RCDATA_LESS_THAN_SIGN_STATE";
        const string RCDATA_END_TAG_OPEN_STATE = "RCDATA_END_TAG_OPEN_STATE";
        const string RCDATA_END_TAG_NAME_STATE = "RCDATA_END_TAG_NAME_STATE";
        const string RAWTEXT_LESS_THAN_SIGN_STATE = "RAWTEXT_LESS_THAN_SIGN_STATE";
        const string RAWTEXT_END_TAG_OPEN_STATE = "RAWTEXT_END_TAG_OPEN_STATE";
        const string RAWTEXT_END_TAG_NAME_STATE = "RAWTEXT_END_TAG_NAME_STATE";
        const string SCRIPT_DATA_LESS_THAN_SIGN_STATE = "SCRIPT_DATA_LESS_THAN_SIGN_STATE";
        const string SCRIPT_DATA_END_TAG_OPEN_STATE = "SCRIPT_DATA_END_TAG_OPEN_STATE";
        const string SCRIPT_DATA_END_TAG_NAME_STATE = "SCRIPT_DATA_END_TAG_NAME_STATE";
        const string SCRIPT_DATA_ESCAPE_START_STATE = "SCRIPT_DATA_ESCAPE_START_STATE";
        const string SCRIPT_DATA_ESCAPE_START_DASH_STATE = "SCRIPT_DATA_ESCAPE_START_DASH_STATE";
        const string SCRIPT_DATA_ESCAPED_STATE = "SCRIPT_DATA_ESCAPED_STATE";
        const string SCRIPT_DATA_ESCAPED_DASH_STATE = "SCRIPT_DATA_ESCAPED_DASH_STATE";
        const string SCRIPT_DATA_ESCAPED_DASH_DASH_STATE = "SCRIPT_DATA_ESCAPED_DASH_DASH_STATE";
        const string SCRIPT_DATA_ESCAPED_LESS_THAN_SIGN_STATE = "SCRIPT_DATA_ESCAPED_LESS_THAN_SIGN_STATE";
        const string SCRIPT_DATA_ESCAPED_END_TAG_OPEN_STATE = "SCRIPT_DATA_ESCAPED_END_TAG_OPEN_STATE";
        const string SCRIPT_DATA_ESCAPED_END_TAG_NAME_STATE = "SCRIPT_DATA_ESCAPED_END_TAG_NAME_STATE";
        const string SCRIPT_DATA_DOUBLE_ESCAPE_START_STATE = "SCRIPT_DATA_DOUBLE_ESCAPE_START_STATE";
        const string SCRIPT_DATA_DOUBLE_ESCAPED_STATE = "SCRIPT_DATA_DOUBLE_ESCAPED_STATE";
        const string SCRIPT_DATA_DOUBLE_ESCAPED_DASH_STATE = "SCRIPT_DATA_DOUBLE_ESCAPED_DASH_STATE";
        const string SCRIPT_DATA_DOUBLE_ESCAPED_DASH_DASH_STATE = "SCRIPT_DATA_DOUBLE_ESCAPED_DASH_DASH_STATE";
        const string SCRIPT_DATA_DOUBLE_ESCAPED_LESS_THAN_SIGN_STATE = "SCRIPT_DATA_DOUBLE_ESCAPED_LESS_THAN_SIGN_STATE";
        const string SCRIPT_DATA_DOUBLE_ESCAPE_END_STATE = "SCRIPT_DATA_DOUBLE_ESCAPE_END_STATE";
        const string BEFORE_ATTRIBUTE_NAME_STATE = "BEFORE_ATTRIBUTE_NAME_STATE";
        const string ATTRIBUTE_NAME_STATE = "ATTRIBUTE_NAME_STATE";
        const string AFTER_ATTRIBUTE_NAME_STATE = "AFTER_ATTRIBUTE_NAME_STATE";
        const string BEFORE_ATTRIBUTE_VALUE_STATE = "BEFORE_ATTRIBUTE_VALUE_STATE";
        const string ATTRIBUTE_VALUE_DOUBLE_QUOTED_STATE = "ATTRIBUTE_VALUE_DOUBLE_QUOTED_STATE";
        const string ATTRIBUTE_VALUE_SINGLE_QUOTED_STATE = "ATTRIBUTE_VALUE_SINGLE_QUOTED_STATE";
        const string ATTRIBUTE_VALUE_UNQUOTED_STATE = "ATTRIBUTE_VALUE_UNQUOTED_STATE";
        const string CHARACTER_REFERENCE_IN_ATTRIBUTE_VALUE_STATE = "CHARACTER_REFERENCE_IN_ATTRIBUTE_VALUE_STATE";
        const string AFTER_ATTRIBUTE_VALUE_QUOTED_STATE = "AFTER_ATTRIBUTE_VALUE_QUOTED_STATE";
        const string SELF_CLOSING_START_TAG_STATE = "SELF_CLOSING_START_TAG_STATE";
        const string BOGUS_COMMENT_STATE = "BOGUS_COMMENT_STATE";
        const string BOGUS_COMMENT_STATE_CONTINUATION = "BOGUS_COMMENT_STATE_CONTINUATION";
        const string MARKUP_DECLARATION_OPEN_STATE = "MARKUP_DECLARATION_OPEN_STATE";
        const string COMMENT_START_STATE = "COMMENT_START_STATE";
        const string COMMENT_START_DASH_STATE = "COMMENT_START_DASH_STATE";
        const string COMMENT_STATE = "COMMENT_STATE";
        const string COMMENT_END_DASH_STATE = "COMMENT_END_DASH_STATE";
        const string COMMENT_END_STATE = "COMMENT_END_STATE";
        const string COMMENT_END_BANG_STATE = "COMMENT_END_BANG_STATE";
        const string DOCTYPE_STATE = "DOCTYPE_STATE";
        const string DOCTYPE_NAME_STATE = "DOCTYPE_NAME_STATE";
        const string AFTER_DOCTYPE_NAME_STATE = "AFTER_DOCTYPE_NAME_STATE";
        const string BEFORE_DOCTYPE_PUBLIC_IDENTIFIER_STATE = "BEFORE_DOCTYPE_PUBLIC_IDENTIFIER_STATE";
        const string DOCTYPE_PUBLIC_IDENTIFIER_DOUBLE_QUOTED_STATE = "DOCTYPE_PUBLIC_IDENTIFIER_DOUBLE_QUOTED_STATE";
        const string DOCTYPE_PUBLIC_IDENTIFIER_SINGLE_QUOTED_STATE = "DOCTYPE_PUBLIC_IDENTIFIER_SINGLE_QUOTED_STATE";
        const string BETWEEN_DOCTYPE_PUBLIC_AND_SYSTEM_IDENTIFIERS_STATE = "BETWEEN_DOCTYPE_PUBLIC_AND_SYSTEM_IDENTIFIERS_STATE";
        const string BEFORE_DOCTYPE_SYSTEM_IDENTIFIER_STATE = "BEFORE_DOCTYPE_SYSTEM_IDENTIFIER_STATE";
        const string DOCTYPE_SYSTEM_IDENTIFIER_DOUBLE_QUOTED_STATE = "DOCTYPE_SYSTEM_IDENTIFIER_DOUBLE_QUOTED_STATE";
        const string DOCTYPE_SYSTEM_IDENTIFIER_SINGLE_QUOTED_STATE = "DOCTYPE_SYSTEM_IDENTIFIER_SINGLE_QUOTED_STATE";
        const string AFTER_DOCTYPE_SYSTEM_IDENTIFIER_STATE = "AFTER_DOCTYPE_SYSTEM_IDENTIFIER_STATE";
        const string BOGUS_DOCTYPE_STATE = "BOGUS_DOCTYPE_STATE";
        const string CDATA_SECTION_STATE = "CDATA_SECTION_STATE";

        // Utils

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsWhitespace(int cp) =>
            cp == CP.SPACE || cp == CP.LINE_FEED || cp == CP.TABULATION || cp == CP.FORM_FEED;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsAsciiDigit(int cp) =>
            cp >= CP.DIGIT_0 && cp <= CP.DIGIT_9;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsAsciiUpper(int cp) =>
            cp >= CP.LATIN_CAPITAL_A && cp <= CP.LATIN_CAPITAL_Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsAsciiLower(int cp) =>
            cp >= CP.LATIN_SMALL_A && cp <= CP.LATIN_SMALL_Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsAsciiLetter(int cp) =>
            IsAsciiLower(cp) || IsAsciiUpper(cp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsAsciiAlphaNumeric(int cp) =>
            IsAsciiLetter(cp) || IsAsciiDigit(cp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsDigit(int cp, bool isHex) =>
            IsAsciiDigit(cp) || isHex && (cp >= CP.LATIN_CAPITAL_A && cp <= CP.LATIN_CAPITAL_F ||
                                          cp >= CP.LATIN_SMALL_A && cp <= CP.LATIN_SMALL_F);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsReservedCodePoint(long cp) =>
            cp >= 0xD800 && cp <= 0xDFFF || cp > 0x10FFFF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsReservedCodePoint(int cp) =>
            cp >= 0xD800 && cp <= 0xDFFF || cp > 0x10FFFF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ToAsciiLowerCodePoint(int cp) =>
            cp + 0x0020;

        // NOTE: String.fromCharCode() function can handle only characters from BMP subset.
        // So, we need to workaround this manually.
        // (see: https://developer.mozilla.org/en-US/docs/JavaScript/Reference/Global_Objects/String/fromCharCode#Getting_it_to_work_with_higher_values)

        static string ToChar(int cp) // TODO consider if cp can be typed as uint
        {
            if (cp <= 0xFFFF)
                return ((char) cp).ToString();

            cp -= 0x10000;
            return new string(new[] { String.fromCharCode((int)(((uint)cp) >> 10) & 0x3FF | 0xD800), String.fromCharCode(0xDC00 | cp & 0x3FF) });
        }

        static char ToAsciiLowerChar(int cp) =>
            String.fromCharCode(ToAsciiLowerCodePoint(cp));

        static int FindNamedEntityTreeBranch(int nodeIx, int cp)
        {
            var branchCount = neTree[++nodeIx];
            var lo = ++nodeIx;
            var hi = lo + branchCount - 1;

            while (lo <= hi)
            {
                var mid = unchecked((int) ((uint) lo + hi) >> 1);
                var midCp = neTree[mid];

                if (midCp < cp)
                    lo = mid + 1;

                else if (midCp > cp)
                    hi = mid - 1;

                else
                    return neTree[mid + branchCount];
            }

            return -1;
        }

        // Fields

        readonly Preprocessor preprocessor;

        readonly List<Token> tokenQueue;
        public bool AllowCData { get; set; }
        public string State { get; set; }
        string returnState;
        List<int> tempBuff;
        int additionalAllowedCp;
        string lastStartTagName;
        int consumedAfterSnapshot;
        bool active;
        DoctypeToken currentDoctypeToken ;
        CommentToken currentCommentToken ;
        StartTagToken currentStartTagToken;
        EndTagToken currentEndTagToken  ;
        CharacterToken currentCharacterToken;
        Token currentToken;
        readonly StringBuilder currentAttrName;
        readonly StringBuilder currentAttrValue;

        DoctypeToken CurrentDoctypeToken
        {
            get => currentDoctypeToken;
            set => SetCurrentTokens(value, doctype: value);
        }

        CommentToken CurrentCommentToken
        {
            get => currentCommentToken;
            set => SetCurrentTokens(value, comment: value);
        }

        TagToken CurrentTagToken => (TagToken) currentStartTagToken ?? currentEndTagToken;

        StartTagToken CurrentStartTagToken
        {
            get => currentStartTagToken;
            set => SetCurrentTokens(value, start: value);
        }

        EndTagToken CurrentEndTagToken
        {
            get => currentEndTagToken;
            set => SetCurrentTokens(value, end: value);
        }

        void SetCurrentTokens(Token value, DoctypeToken doctype = null, CommentToken comment = null, StartTagToken start = null, EndTagToken end = null)
        {
            currentToken = value;
            currentDoctypeToken = doctype;
            currentCommentToken = comment;
            currentStartTagToken = start;
            currentEndTagToken = end;
        }

        // Tokenizer

        public Tokenizer()
        {
            this.preprocessor = new Preprocessor();

            this.tokenQueue = new List<Token>();

            this.AllowCData = false;

            this.State = DATA_STATE;
            this.returnState = "";

            this.tempBuff = new List<int>();
            this.additionalAllowedCp = 0; // void 0
            this.lastStartTagName = "";

            this.consumedAfterSnapshot = -1;
            this.active = false;

            this.currentCharacterToken = null;
            this.currentToken = null;
            this.currentAttrName = new StringBuilder();
            this.currentAttrValue = new StringBuilder();
        }

        static Dictionary<string, Action<Tokenizer, int>> _actionByState;

        Action<Tokenizer, int> this[string state] =>
            (_actionByState ?? (_actionByState = CreateStateActionMap()))[state];

        // Tokenizer initial states for different modes

        public static class MODE
        {
            public static string DATA = DATA_STATE;
            public static string RCDATA = RCDATA_STATE;
            public static string RAWTEXT = RAWTEXT_STATE;
            public static string SCRIPT_DATA = SCRIPT_DATA_STATE;
            public static string PLAINTEXT = PLAINTEXT_STATE;
        }

        // Static

        public static string GetTokenAttr(TagToken token, string attrName)
        {
            for (var i = token.Attrs.Count - 1; i >= 0; i--)
            {
                if (token.Attrs[i].Name == attrName)
                    return token.Attrs[i].Value;
            }

            return null;
        }

        // API

        public Token GetNextToken()
        {
            while (this.tokenQueue.Count == 0 && this.active)
            {
                this.HibernationSnapshot();

                var cp = this.Consume();

                if (!this.EnsureHibernation())
                    this[this.State](this, cp);
            }

            return tokenQueue.Shift();
        }

        public void Write(string chunk, bool isLastChunk)
        {
            this.active = true;
            this.preprocessor.Write(chunk, isLastChunk);
        }

        public void InsertHtmlAtCurrentPos(string chunk)
        {
            this.active = true;
            this.preprocessor.InsertHtmlAtCurrentPos(chunk);
        }

        // Hibernation

        public void HibernationSnapshot()
        {
            this.consumedAfterSnapshot = 0;
        }

        public bool EnsureHibernation()
        {
            if (this.preprocessor.EndOfChunkHit)
            {
                for (; this.consumedAfterSnapshot > 0; this.consumedAfterSnapshot--)
                    this.preprocessor.Retreat();

                this.active = false;
                this.tokenQueue.Push(HibernationToken.Instance);

                return true;
            }

            return false;
        }

        // Consumption

        public int Consume()
        {
            this.consumedAfterSnapshot++;
            return this.preprocessor.Advance();
        }

        public void Unconsume()
        {
            this.consumedAfterSnapshot--;
            this.preprocessor.Retreat();
        }

        void UnconsumeSeveral(int count)
        {
            while (count-- > 0)
                this.Unconsume();
        }

        void ReconsumeInState(string state)
        {
            this.State = state;
            this.Unconsume();
        }

        bool ConsumeSubsequentIfMatch(int[] pattern, int startCp, bool caseSensitive)
        {
            var consumedCount = 0;
            var isMatch = true;
            var patternLength = pattern.Length;
            var patternPos = 0;
            var cp = startCp;
            int? patternCp = null;// void 0;

            for (; patternPos < patternLength; patternPos++)
            {
                if (patternPos > 0)
                {
                    cp = this.Consume();
                    consumedCount++;
                }

                if (cp == CP.EOF)
                {
                    isMatch = false;
                    break;
                }

                patternCp = pattern[patternPos];

                if (cp != patternCp && (caseSensitive || cp != ToAsciiLowerCodePoint(patternCp.Value)))
                {
                    isMatch = false;
                    break;
                }
            }

            if (!isMatch)
                this.UnconsumeSeveral(consumedCount);

            return isMatch;
        }

        // Lookahead

        int Lookahead()
        {
            var cp = this.Consume();

            this.Unconsume();

            return cp;
        }

        // Temp buffer

        bool IsTempBufferEqualToScriptString()
        {
            if (this.tempBuff.Count != CPS.SCRIPT_STRING.Length)
                return false;

            for (var i = 0; i < this.tempBuff.Count; i++)
            {
                if (this.tempBuff[i] != CPS.SCRIPT_STRING[i])
                    return false;
            }

            return true;
        }

        // Token creation

        void CreateStartTagToken()
        {
            this.CurrentStartTagToken = new StartTagToken("", false, new List<Attr>());
        }

        void CreateEndTagToken()
        {
            this.CurrentEndTagToken = new EndTagToken("", new List<Attr>());
        }

        void CreateCommentToken()
        {
            this.CurrentCommentToken = new CommentToken("");
        }

        void CreateDoctypeToken(string initialName)
        {
            this.CurrentDoctypeToken = new DoctypeToken(initialName);
        }

        void CreateCharacterToken(TokenType type, string ch)
        {
            this.currentCharacterToken = new CharacterToken(type, ch[0]);
        }

        // Tag attributes

        void CreateAttr(string attrNameFirstCh) // TODO Check if string or char
        {
            this.currentAttrName.Clear().Append(attrNameFirstCh);
            this.currentAttrValue.Clear();
        }

        bool IsDuplicateAttr(string name)
        {
            return GetTokenAttr(this.CurrentTagToken, name) != null;
        }

        void LeaveAttrName(string toState)
        {
            this.State = toState;

            var name = this.currentAttrName.ToString();
            if (!this.IsDuplicateAttr(name))
                this.CurrentTagToken.Attrs.Push(new Attr(name, this.currentAttrValue.ToString()));
        }

        void LeaveAttrValue(string toState)
        {
            var attrs = this.CurrentTagToken.Attrs;
            var index = attrs.Count - 1;
            attrs[index] = attrs[index].WithValue(currentAttrValue.ToString());
            this.State = toState;
        }

        // Appropriate end tag token
        // (see: http://www.whatwg.org/specs/web-apps/current-work/multipage/tokenization.html#appropriate-end-tag-token)

        bool IsAppropriateEndTagToken()
        {
            return this.lastStartTagName == this.CurrentEndTagToken.TagName;
        }

        // Token emission

        void EmitCurrentToken()
        {
            this.EmitCurrentCharacterToken();

            // NOTE: store emited start tag's tagName to determine is the following end tag token is appropriate.
            if (this.currentToken is StartTagToken startTagToken)
                this.lastStartTagName = startTagToken.TagName;

            this.tokenQueue.Push(this.currentToken);
            this.currentToken = null;
        }

        void EmitCurrentCharacterToken()
        {
            if (this.currentCharacterToken != null)
            {
                this.tokenQueue.Push(this.currentCharacterToken);
                this.currentCharacterToken = null;
            }
        }

        void EmitEofToken()
        {
            this.EmitCurrentCharacterToken();
            this.tokenQueue.Push(EofToken.Instance);
        }

        // Characters emission

        // OPTIMIZATION: specification uses only one type of character tokens (one token per character).
        // This causes a huge memory overhead and a lot of unnecessary parser loops. parse5 uses 3 groups of characters.
        // If we have a sequence of characters that belong to the same group, parser can process it
        // as a single solid character token.
        // So, there are 3 types of character tokens in parse5:
        // 1)NULL_CHARACTER_TOKEN - \u0000-character sequences (e.g. '\u0000\u0000\u0000')
        // 2)WHITESPACE_CHARACTER_TOKEN - any whitespace/new-line character sequences (e.g. '\n  \r\t   \f')
        // 3)CHARACTER_TOKEN - any character sequence which don't belong to groups 1 and 2 (e.g. 'abcdef1234@@#$%^')

        void AppendCharToCurrentCharacterToken(TokenType type, string ch)
        {
            if (this.currentCharacterToken != null && this.currentCharacterToken.Type != type)
                this.EmitCurrentCharacterToken();

            if (this.currentCharacterToken != null)
                this.currentCharacterToken.Chars += ch;

            else
                this.CreateCharacterToken(type, ch);
        }

        void EmitCodePoint(int cp)
        {
            var type = TokenType.CHARACTER_TOKEN;

            if (IsWhitespace(cp))
                type = TokenType.WHITESPACE_CHARACTER_TOKEN;

            else if (cp == CP.NULL)
                type = TokenType.NULL_CHARACTER_TOKEN;

            this.AppendCharToCurrentCharacterToken(type, ToChar(cp));
        }

        void EmitSeveralCodePoints(IEnumerable<int> codePoints)
        {
            foreach (var cp in codePoints)
                this.EmitCodePoint(cp);
        }

        // NOTE: used then we emit character explicitly. This is always a non-whitespace and a non-null character.
        // So we can avoid additional checks here.

        void EmitChar(char ch) => EmitChar(ch.ToString());
        void EmitChar(string ch)
        {
            this.AppendCharToCurrentCharacterToken(TokenType.CHARACTER_TOKEN, ch);
        }

        // Character reference tokenization

        int ConsumeNumericEntity(bool isHex)
        {
            var digits = "";
            var nextCp = 0;

            do
            {
                digits += ToChar(this.Consume());
                nextCp = this.Lookahead();
            } while (nextCp != CP.EOF && IsDigit(nextCp, isHex));

            if (this.Lookahead() == CP.SEMICOLON)
                this.Consume();

            var referencedCpLong = long.Parse(digits, isHex ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None);
            if (IsReservedCodePoint(referencedCpLong))
                return CP.REPLACEMENT_CHARACTER;

            var referencedCp = unchecked((int) referencedCpLong);

            if (NumericEntityReplacements.TryGetValue(referencedCp, out var replacement))
                return replacement;

            if (IsReservedCodePoint(referencedCp))
                return CP.REPLACEMENT_CHARACTER;

            return referencedCp;
        }

        // NOTE: for the details on this algorithm see
        // https://github.com/inikulin/parse5/tree/master/scripts/generate_named_entity_data/README.md

        int[] ConsumeNamedEntity(bool inAttr)
        {
            int[] referencedCodePoints = null;
            var referenceSize = 0;
            var cp = 0;
            var consumedCount = 0;
            var semicolonTerminated = false;

            for (var i = 0; i > -1;)
            {
                var current = neTree[i];
                var inNode = current < MaxBranchMarkerValue;
                var nodeWithData = inNode && (current & HasDataFlag) == HasDataFlag;

                if (nodeWithData)
                {
                    referencedCodePoints = (current & DataDupletFlag) == DataDupletFlag ? new[] { neTree[++i], neTree[++i] } : new[] { neTree[++i] };
                    referenceSize = consumedCount;

                    if (cp == CP.SEMICOLON)
                    {
                        semicolonTerminated = true;
                        break;
                    }

                }

                cp = this.Consume();

                consumedCount++;

                if (cp == CP.EOF)
                    break;

                if (inNode)
                    i = (current & HasBranchesFlag) == HasBranchesFlag ? FindNamedEntityTreeBranch(i, cp) : -1;

                else
                    i = cp == current ? ++i : -1;
            }

            if (referencedCodePoints != null)
            {
                if (!semicolonTerminated)
                {
                    // NOTE: unconsume excess (e.g. 'it' in '&notit')
                    this.UnconsumeSeveral(consumedCount - referenceSize);

                    // NOTE: If the character reference is being consumed as part of an attribute and the next character
                    // is either a U+003D EQUALS SIGN character (=) or an alphanumeric ASCII character, then, for historical
                    // reasons, all the characters that were matched after the U+0026 AMPERSAND character (&) must be
                    // unconsumed, and nothing is returned.
                    // However, if this next character is in fact a U+003D EQUALS SIGN character (=), then this is a
                    // parse error, because some legacy user agents will misinterpret the markup in those cases.
                    // (see: http://www.whatwg.org/specs/web-apps/current-work/multipage/tokenization.html#tokenizing-character-references)
                    if (inAttr)
                    {
                        var nextCp = this.Lookahead();

                        if (nextCp == CP.EQUALS_SIGN || IsAsciiAlphaNumeric(nextCp))
                        {
                            this.UnconsumeSeveral(referenceSize);
                            return null;
                        }
                    }
                }

                return referencedCodePoints;
            }

            this.UnconsumeSeveral(consumedCount);

            return null;
        }

        int[] ConsumeCharacterReference(int startCp, bool inAttr)
        {
            if (IsWhitespace(startCp) || startCp == CP.GREATER_THAN_SIGN ||
                startCp == CP.AMPERSAND || startCp == this.additionalAllowedCp || startCp == CP.EOF)
            {
                // NOTE: not a character reference. No characters are consumed, and nothing is returned.
                this.Unconsume();
                return null;
            }

            if (startCp == CP.NUMBER_SIGN)
            {
                // NOTE: we have a numeric entity candidate, now we should determine if it's hex or decimal
                var isHex = false;
                var nextCp = this.Lookahead();

                if (nextCp == CP.LATIN_SMALL_X || nextCp == CP.LATIN_CAPITAL_X)
                {
                    this.Consume();
                    isHex = true;
                }

                nextCp = this.Lookahead();

                // NOTE: if we have at least one digit this is a numeric entity for sure, so we consume it
                if (nextCp != CP.EOF && IsDigit(nextCp, isHex))
                    return new[] { this.ConsumeNumericEntity(isHex) };

                // NOTE: otherwise this is a bogus number entity and a parse error. Unconsume the number sign
                // and the 'x'-character if appropriate.
                this.UnconsumeSeveral(isHex ? 2 : 1);
                return null;
            }

            this.Unconsume();

            return this.ConsumeNamedEntity(inAttr);
        }

        static Dictionary<string, Action<Tokenizer, int>> CreateStateActionMap()
        {
            var _ = new Dictionary<string, Action<Tokenizer, int>>();

            // 12.2.4.1 Data state
            // ------------------------------------------------------------------
            _[DATA_STATE] = DataState; void DataState(Tokenizer @this, int cp)
            {
                @this.preprocessor.DropParsedChunk();

                if (cp == CP.AMPERSAND)
                    @this.State = CHARACTER_REFERENCE_IN_DATA_STATE;

                else if (cp == CP.LESS_THAN_SIGN)
                    @this.State = TAG_OPEN_STATE;

                else if (cp == CP.NULL)
                    @this.EmitCodePoint(cp);

                else if (cp == CP.EOF)
                    @this.EmitEofToken();

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.2 Character reference in data state
            // ------------------------------------------------------------------
            _[CHARACTER_REFERENCE_IN_DATA_STATE] = CharacterReferenceInDataState; void CharacterReferenceInDataState(Tokenizer @this, int cp)
            {
                @this.additionalAllowedCp = 0; // void 0;

                var referencedCodePoints = @this.ConsumeCharacterReference(cp, false);

                if (!@this.EnsureHibernation())
                {
                    if (referencedCodePoints != null)
                        @this.EmitSeveralCodePoints(referencedCodePoints);

                    else
                        @this.EmitChar('&');

                    @this.State = DATA_STATE;
                }
            }

            // 12.2.4.3 RCDATA state
            // ------------------------------------------------------------------
            _[RCDATA_STATE] = RcdataState; void RcdataState(Tokenizer @this, int cp)
            {
                @this.preprocessor.DropParsedChunk();

                if (cp == CP.AMPERSAND)
                    @this.State = CHARACTER_REFERENCE_IN_RCDATA_STATE;

                else if (cp == CP.LESS_THAN_SIGN)
                    @this.State = RCDATA_LESS_THAN_SIGN_STATE;

                else if (cp == CP.NULL)
                    @this.EmitChar((char)CP.REPLACEMENT_CHARACTER);

                else if (cp == CP.EOF)
                    @this.EmitEofToken();

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.4 Character reference in RCDATA state
            // ------------------------------------------------------------------
            _[CHARACTER_REFERENCE_IN_RCDATA_STATE] = CharacterReferenceInRcdataState; void CharacterReferenceInRcdataState(Tokenizer @this, int cp)
            {
                @this.additionalAllowedCp = 0; // void 0;

                var referencedCodePoints = @this.ConsumeCharacterReference(cp, false);

                if (!@this.EnsureHibernation())
                {
                    if (referencedCodePoints != null)
                        @this.EmitSeveralCodePoints(referencedCodePoints);

                    else
                        @this.EmitChar('&');

                    @this.State = RCDATA_STATE;
                }
            }

            // 12.2.4.5 RAWTEXT state
            // ------------------------------------------------------------------
            _[RAWTEXT_STATE] = RawtextState; void RawtextState(Tokenizer @this, int cp)
            {
                @this.preprocessor.DropParsedChunk();

                if (cp == CP.LESS_THAN_SIGN)
                    @this.State = RAWTEXT_LESS_THAN_SIGN_STATE;

                else if (cp == CP.NULL)
                    @this.EmitChar((char)CP.REPLACEMENT_CHARACTER);

                else if (cp == CP.EOF)
                    @this.EmitEofToken();

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.6 Script data state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_STATE] = ScriptDataState; void ScriptDataState(Tokenizer @this, int cp)
            {
                @this.preprocessor.DropParsedChunk();

                if (cp == CP.LESS_THAN_SIGN)
                    @this.State = SCRIPT_DATA_LESS_THAN_SIGN_STATE;

                else if (cp == CP.NULL)
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));

                else if (cp == CP.EOF)
                    @this.EmitEofToken();

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.7 PLAINTEXT state
            // ------------------------------------------------------------------
            _[PLAINTEXT_STATE] = PlaintextState; void PlaintextState(Tokenizer @this, int cp)
            {
                @this.preprocessor.DropParsedChunk();

                if (cp == CP.NULL)
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));

                else if (cp == CP.EOF)
                    @this.EmitEofToken();

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.8 Tag open state
            // ------------------------------------------------------------------
            _[TAG_OPEN_STATE] = TagOpenState; void TagOpenState(Tokenizer @this, int cp)
            {
                if (cp == CP.EXCLAMATION_MARK)
                    @this.State = MARKUP_DECLARATION_OPEN_STATE;

                else if (cp == CP.SOLIDUS)
                    @this.State = END_TAG_OPEN_STATE;

                else if (IsAsciiLetter(cp))
                {
                    @this.CreateStartTagToken();
                    @this.ReconsumeInState(TAG_NAME_STATE);
                }

                else if (cp == CP.QUESTION_MARK)
                    @this.ReconsumeInState(BOGUS_COMMENT_STATE);

                else
                {
                    @this.EmitChar('<');
                    @this.ReconsumeInState(DATA_STATE);
                }
            }

            // 12.2.4.9 End tag open state
            // ------------------------------------------------------------------
            _[END_TAG_OPEN_STATE] = EndTagOpenState; void EndTagOpenState(Tokenizer @this, int cp)
            {
                if (IsAsciiLetter(cp))
                {
                    @this.CreateEndTagToken();
                    @this.ReconsumeInState(TAG_NAME_STATE);
                }

                else if (cp == CP.GREATER_THAN_SIGN)
                    @this.State = DATA_STATE;

                else if (cp == CP.EOF)
                {
                    @this.ReconsumeInState(DATA_STATE);
                    @this.EmitChar('<');
                    @this.EmitChar('/');
                }

                else
                    @this.ReconsumeInState(BOGUS_COMMENT_STATE);
            }

            // 12.2.4.10 Tag name state
            // ------------------------------------------------------------------
            _[TAG_NAME_STATE] = TagNameState; void TagNameState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    @this.State = BEFORE_ATTRIBUTE_NAME_STATE;

                else if (cp == CP.SOLIDUS)
                    @this.State = SELF_CLOSING_START_TAG_STATE;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (IsAsciiUpper(cp))
                    @this.CurrentTagToken.TagName += ToAsciiLowerChar(cp);

                else if (cp == CP.NULL)
                    @this.CurrentTagToken.TagName += CP.REPLACEMENT_CHARACTER;

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.CurrentTagToken.TagName += ToChar(cp);
            }

            // 12.2.4.11 RCDATA less-than sign state
            // ------------------------------------------------------------------
            _[RCDATA_LESS_THAN_SIGN_STATE] = RcdataLessThanSignState; void RcdataLessThanSignState(Tokenizer @this, int cp)
            {
                if (cp == CP.SOLIDUS)
                {
                    @this.tempBuff = new List<int>();
                    @this.State = RCDATA_END_TAG_OPEN_STATE;
                }

                else
                {
                    @this.EmitChar('<');
                    @this.ReconsumeInState(RCDATA_STATE);
                }
            }

            // 12.2.4.12 RCDATA end tag open state
            // ------------------------------------------------------------------
            _[RCDATA_END_TAG_OPEN_STATE] = RcdataEndTagOpenState; void RcdataEndTagOpenState(Tokenizer @this, int cp)
            {
                if (IsAsciiLetter(cp))
                {
                    @this.CreateEndTagToken();
                    @this.ReconsumeInState(RCDATA_END_TAG_NAME_STATE);
                }

                else
                {
                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.ReconsumeInState(RCDATA_STATE);
                }
            }

            // 12.2.4.13 RCDATA end tag name state
            // ------------------------------------------------------------------
            _[RCDATA_END_TAG_NAME_STATE] = RcdataEndTagNameState; void RcdataEndTagNameState(Tokenizer @this, int cp)
            {
                if (IsAsciiUpper(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToAsciiLowerChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else if (IsAsciiLower(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else
                {
                    if (@this.IsAppropriateEndTagToken())
                    {
                        if (IsWhitespace(cp))
                        {
                            @this.State = BEFORE_ATTRIBUTE_NAME_STATE;
                            return;
                        }

                        if (cp == CP.SOLIDUS)
                        {
                            @this.State = SELF_CLOSING_START_TAG_STATE;
                            return;
                        }

                        if (cp == CP.GREATER_THAN_SIGN)
                        {
                            @this.State = DATA_STATE;
                            @this.EmitCurrentToken();
                            return;
                        }
                    }

                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.EmitSeveralCodePoints(@this.tempBuff);
                    @this.ReconsumeInState(RCDATA_STATE);
                }
            }

            // 12.2.4.14 RAWTEXT less-than sign state
            // ------------------------------------------------------------------
            _[RAWTEXT_LESS_THAN_SIGN_STATE] = RawtextLessThanSignState; void RawtextLessThanSignState(Tokenizer @this, int cp)
            {
                if (cp == CP.SOLIDUS)
                {
                    @this.tempBuff = new List<int>();
                    @this.State = RAWTEXT_END_TAG_OPEN_STATE;
                }

                else
                {
                    @this.EmitChar('<');
                    @this.ReconsumeInState(RAWTEXT_STATE);
                }
            }

            // 12.2.4.15 RAWTEXT end tag open state
            // ------------------------------------------------------------------
            _[RAWTEXT_END_TAG_OPEN_STATE] = RawtextEndTagOpenState; void RawtextEndTagOpenState(Tokenizer @this, int cp)
            {
                if (IsAsciiLetter(cp))
                {
                    @this.CreateEndTagToken();
                    @this.ReconsumeInState(RAWTEXT_END_TAG_NAME_STATE);
                }

                else
                {
                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.ReconsumeInState(RAWTEXT_STATE);
                }
            }

            // 12.2.4.16 RAWTEXT end tag name state
            // ------------------------------------------------------------------
            _[RAWTEXT_END_TAG_NAME_STATE] = RawtextEndTagNameState; void RawtextEndTagNameState(Tokenizer @this, int cp)
            {
                if (IsAsciiUpper(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToAsciiLowerChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else if (IsAsciiLower(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else
                {
                    if (@this.IsAppropriateEndTagToken())
                    {
                        if (IsWhitespace(cp))
                        {
                            @this.State = BEFORE_ATTRIBUTE_NAME_STATE;
                            return;
                        }

                        if (cp == CP.SOLIDUS)
                        {
                            @this.State = SELF_CLOSING_START_TAG_STATE;
                            return;
                        }

                        if (cp == CP.GREATER_THAN_SIGN)
                        {
                            @this.EmitCurrentToken();
                            @this.State = DATA_STATE;
                            return;
                        }
                    }

                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.EmitSeveralCodePoints(@this.tempBuff);
                    @this.ReconsumeInState(RAWTEXT_STATE);
                }
            }

            // 12.2.4.17 Script data less-than sign state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_LESS_THAN_SIGN_STATE] = ScriptDataLessThanSignState; void ScriptDataLessThanSignState(Tokenizer @this, int cp)
            {
                if (cp == CP.SOLIDUS)
                {
                    @this.tempBuff = new List<int>();
                    @this.State = SCRIPT_DATA_END_TAG_OPEN_STATE;
                }

                else if (cp == CP.EXCLAMATION_MARK)
                {
                    @this.State = SCRIPT_DATA_ESCAPE_START_STATE;
                    @this.EmitChar('<');
                    @this.EmitChar('!');
                }

                else
                {
                    @this.EmitChar('<');
                    @this.ReconsumeInState(SCRIPT_DATA_STATE);
                }
            }

            // 12.2.4.18 Script data end tag open state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_END_TAG_OPEN_STATE] = ScriptDataEndTagOpenState; void ScriptDataEndTagOpenState(Tokenizer @this, int cp)
            {
                if (IsAsciiLetter(cp))
                {
                    @this.CreateEndTagToken();
                    @this.ReconsumeInState(SCRIPT_DATA_END_TAG_NAME_STATE);
                }

                else
                {
                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.ReconsumeInState(SCRIPT_DATA_STATE);
                }
            }

            // 12.2.4.19 Script data end tag name state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_END_TAG_NAME_STATE] = ScriptDataEndTagNameState; void ScriptDataEndTagNameState(Tokenizer @this, int cp)
            {
                if (IsAsciiUpper(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToAsciiLowerChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else if (IsAsciiLower(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else
                {
                    if (@this.IsAppropriateEndTagToken())
                    {
                        if (IsWhitespace(cp))
                        {
                            @this.State = BEFORE_ATTRIBUTE_NAME_STATE;
                            return;
                        }

                        else if (cp == CP.SOLIDUS)
                        {
                            @this.State = SELF_CLOSING_START_TAG_STATE;
                            return;
                        }

                        else if (cp == CP.GREATER_THAN_SIGN)
                        {
                            @this.EmitCurrentToken();
                            @this.State = DATA_STATE;
                            return;
                        }
                    }

                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.EmitSeveralCodePoints(@this.tempBuff);
                    @this.ReconsumeInState(SCRIPT_DATA_STATE);
                }
            }

            // 12.2.4.20 Script data escape start state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPE_START_STATE] = ScriptDataEscapeStartState; void ScriptDataEscapeStartState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.State = SCRIPT_DATA_ESCAPE_START_DASH_STATE;
                    @this.EmitChar('-');
                }

                else
                    @this.ReconsumeInState(SCRIPT_DATA_STATE);
            }

            // 12.2.4.21 Script data escape start dash state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPE_START_DASH_STATE] = ScriptDataEscapeStartDashState; void ScriptDataEscapeStartDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.State = SCRIPT_DATA_ESCAPED_DASH_DASH_STATE;
                    @this.EmitChar('-');
                }

                else
                    @this.ReconsumeInState(SCRIPT_DATA_STATE);
            }

            // 12.2.4.22 Script data escaped state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPED_STATE] = ScriptDataEscapedState; void ScriptDataEscapedState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.State = SCRIPT_DATA_ESCAPED_DASH_STATE;
                    @this.EmitChar('-');
                }

                else if (cp == CP.LESS_THAN_SIGN)
                    @this.State = SCRIPT_DATA_ESCAPED_LESS_THAN_SIGN_STATE;

                else if (cp == CP.NULL)
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.23 Script data escaped dash state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPED_DASH_STATE] = ScriptDataEscapedDashState; void ScriptDataEscapedDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.State = SCRIPT_DATA_ESCAPED_DASH_DASH_STATE;
                    @this.EmitChar('-');
                }

                else if (cp == CP.LESS_THAN_SIGN)
                    @this.State = SCRIPT_DATA_ESCAPED_LESS_THAN_SIGN_STATE;

                else if (cp == CP.NULL)
                {
                    @this.State = SCRIPT_DATA_ESCAPED_STATE;
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                {
                    @this.State = SCRIPT_DATA_ESCAPED_STATE;
                    @this.EmitCodePoint(cp);
                }
            }

            // 12.2.4.24 Script data escaped dash dash state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPED_DASH_DASH_STATE] = ScriptDataEscapedDashDashState; void ScriptDataEscapedDashDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                    @this.EmitChar('-');

                else if (cp == CP.LESS_THAN_SIGN)
                    @this.State = SCRIPT_DATA_ESCAPED_LESS_THAN_SIGN_STATE;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = SCRIPT_DATA_STATE;
                    @this.EmitChar('>');
                }

                else if (cp == CP.NULL)
                {
                    @this.State = SCRIPT_DATA_ESCAPED_STATE;
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                {
                    @this.State = SCRIPT_DATA_ESCAPED_STATE;
                    @this.EmitCodePoint(cp);
                }
            }

            // 12.2.4.25 Script data escaped less-than sign state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPED_LESS_THAN_SIGN_STATE] = ScriptDataEscapedLessThanSignState; void ScriptDataEscapedLessThanSignState(Tokenizer @this, int cp)
            {
                if (cp == CP.SOLIDUS)
                {
                    @this.tempBuff = new List<int>();
                    @this.State = SCRIPT_DATA_ESCAPED_END_TAG_OPEN_STATE;
                }

                else if (IsAsciiLetter(cp))
                {
                    @this.tempBuff = new List<int>();
                    @this.EmitChar('<');
                    @this.ReconsumeInState(SCRIPT_DATA_DOUBLE_ESCAPE_START_STATE);
                }

                else
                {
                    @this.EmitChar('<');
                    @this.ReconsumeInState(SCRIPT_DATA_ESCAPED_STATE);
                }
            }

            // 12.2.4.26 Script data escaped end tag open state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPED_END_TAG_OPEN_STATE] = ScriptDataEscapedEndTagOpenState; void ScriptDataEscapedEndTagOpenState(Tokenizer @this, int cp)
            {
                if (IsAsciiLetter(cp))
                {
                    @this.CreateEndTagToken();
                    @this.ReconsumeInState(SCRIPT_DATA_ESCAPED_END_TAG_NAME_STATE);
                }

                else
                {
                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.ReconsumeInState(SCRIPT_DATA_ESCAPED_STATE);
                }
            }

            // 12.2.4.27 Script data escaped end tag name state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_ESCAPED_END_TAG_NAME_STATE] = ScriptDataEscapedEndTagNameState; void ScriptDataEscapedEndTagNameState(Tokenizer @this, int cp)
            {
                if (IsAsciiUpper(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToAsciiLowerChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else if (IsAsciiLower(cp))
                {
                    @this.CurrentEndTagToken.TagName += ToChar(cp);
                    @this.tempBuff.Push(cp);
                }

                else
                {
                    if (@this.IsAppropriateEndTagToken())
                    {
                        if (IsWhitespace(cp))
                        {
                            @this.State = BEFORE_ATTRIBUTE_NAME_STATE;
                            return;
                        }

                        if (cp == CP.SOLIDUS)
                        {
                            @this.State = SELF_CLOSING_START_TAG_STATE;
                            return;
                        }

                        if (cp == CP.GREATER_THAN_SIGN)
                        {
                            @this.EmitCurrentToken();
                            @this.State = DATA_STATE;
                            return;
                        }
                    }

                    @this.EmitChar('<');
                    @this.EmitChar('/');
                    @this.EmitSeveralCodePoints(@this.tempBuff);
                    @this.ReconsumeInState(SCRIPT_DATA_ESCAPED_STATE);
                }
            }

            // 12.2.4.28 Script data double escape start state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_DOUBLE_ESCAPE_START_STATE] = ScriptDataDoubleEscapeStartState; void ScriptDataDoubleEscapeStartState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp) || cp == CP.SOLIDUS || cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = @this.IsTempBufferEqualToScriptString() ? SCRIPT_DATA_DOUBLE_ESCAPED_STATE : SCRIPT_DATA_ESCAPED_STATE;
                    @this.EmitCodePoint(cp);
                }

                else if (IsAsciiUpper(cp))
                {
                    @this.tempBuff.Push(ToAsciiLowerCodePoint(cp));
                    @this.EmitCodePoint(cp);
                }

                else if (IsAsciiLower(cp))
                {
                    @this.tempBuff.Push(cp);
                    @this.EmitCodePoint(cp);
                }

                else
                    @this.ReconsumeInState(SCRIPT_DATA_ESCAPED_STATE);
            }

            // 12.2.4.29 Script data double escaped state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_DOUBLE_ESCAPED_STATE] = ScriptDataDoubleEscapedState; void ScriptDataDoubleEscapedState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_DASH_STATE;
                    @this.EmitChar('-');
                }

                else if (cp == CP.LESS_THAN_SIGN)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_LESS_THAN_SIGN_STATE;
                    @this.EmitChar('<');
                }

                else if (cp == CP.NULL)
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.EmitCodePoint(cp);
            }

            // 12.2.4.30 Script data double escaped dash state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_DOUBLE_ESCAPED_DASH_STATE] = ScriptDataDoubleEscapedDashState; void ScriptDataDoubleEscapedDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_DASH_DASH_STATE;
                    @this.EmitChar('-');
                }

                else if (cp == CP.LESS_THAN_SIGN)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_LESS_THAN_SIGN_STATE;
                    @this.EmitChar('<');
                }

                else if (cp == CP.NULL)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_STATE;
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_STATE;
                    @this.EmitCodePoint(cp);
                }
            }

            // 12.2.4.31 Script data double escaped dash dash state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_DOUBLE_ESCAPED_DASH_DASH_STATE] = ScriptDataDoubleEscapedDashDashState; void ScriptDataDoubleEscapedDashDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                    @this.EmitChar('-');

                else if (cp == CP.LESS_THAN_SIGN)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_LESS_THAN_SIGN_STATE;
                    @this.EmitChar('<');
                }

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = SCRIPT_DATA_STATE;
                    @this.EmitChar('>');
                }

                else if (cp == CP.NULL)
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_STATE;
                    @this.EmitChar(((char)CP.REPLACEMENT_CHARACTER));
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                {
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPED_STATE;
                    @this.EmitCodePoint(cp);
                }
            }

            // 12.2.4.32 Script data double escaped less-than sign state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_DOUBLE_ESCAPED_LESS_THAN_SIGN_STATE] = ScriptDataDoubleEscapedLessThanSignState; void ScriptDataDoubleEscapedLessThanSignState(Tokenizer @this, int cp)
            {
                if (cp == CP.SOLIDUS)
                {
                    @this.tempBuff = new List<int>();
                    @this.State = SCRIPT_DATA_DOUBLE_ESCAPE_END_STATE;
                    @this.EmitChar('/');
                }

                else
                    @this.ReconsumeInState(SCRIPT_DATA_DOUBLE_ESCAPED_STATE);
            }

            // 12.2.4.33 Script data double escape end state
            // ------------------------------------------------------------------
            _[SCRIPT_DATA_DOUBLE_ESCAPE_END_STATE] = ScriptDataDoubleEscapeEndState; void ScriptDataDoubleEscapeEndState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp) || cp == CP.SOLIDUS || cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = @this.IsTempBufferEqualToScriptString() ? SCRIPT_DATA_ESCAPED_STATE : SCRIPT_DATA_DOUBLE_ESCAPED_STATE;

                    @this.EmitCodePoint(cp);
                }

                else if (IsAsciiUpper(cp))
                {
                    @this.tempBuff.Push(ToAsciiLowerCodePoint(cp));
                    @this.EmitCodePoint(cp);
                }

                else if (IsAsciiLower(cp))
                {
                    @this.tempBuff.Push(cp);
                    @this.EmitCodePoint(cp);
                }

                else
                    @this.ReconsumeInState(SCRIPT_DATA_DOUBLE_ESCAPED_STATE);
            }

            // 12.2.4.34 Before attribute name state
            // ------------------------------------------------------------------
            _[BEFORE_ATTRIBUTE_NAME_STATE] = BeforeAttributeNameState; void BeforeAttributeNameState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.SOLIDUS || cp == CP.GREATER_THAN_SIGN || cp == CP.EOF)
                    @this.ReconsumeInState(AFTER_ATTRIBUTE_NAME_STATE);

                else if (cp == CP.EQUALS_SIGN)
                {
                    @this.CreateAttr("=");
                    @this.State = ATTRIBUTE_NAME_STATE;
                }

                else
                {
                    @this.CreateAttr("");
                    @this.ReconsumeInState(ATTRIBUTE_NAME_STATE);
                }
            }

            // 12.2.4.35 Attribute name state
            // ------------------------------------------------------------------
            _[ATTRIBUTE_NAME_STATE] = AttributeNameState; void AttributeNameState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp) || cp == CP.SOLIDUS || cp == CP.GREATER_THAN_SIGN || cp == CP.EOF)
                {
                    @this.LeaveAttrName(AFTER_ATTRIBUTE_NAME_STATE);
                    @this.Unconsume();
                }

                else if (cp == CP.EQUALS_SIGN)
                    @this.LeaveAttrName(BEFORE_ATTRIBUTE_VALUE_STATE);

                else if (IsAsciiUpper(cp))
                    @this.currentAttrName.Append(ToAsciiLowerChar(cp));

                else if (cp == CP.QUOTATION_MARK || cp == CP.APOSTROPHE || cp == CP.LESS_THAN_SIGN)
                    @this.currentAttrName.Append(ToChar(cp));

                else if (cp == CP.NULL)
                    @this.currentAttrName.Append(CP.REPLACEMENT_CHARACTER);

                else
                    @this.currentAttrName.Append(ToChar(cp));
            }

            // 12.2.4.36 After attribute name state
            // ------------------------------------------------------------------
            _[AFTER_ATTRIBUTE_NAME_STATE] = AfterAttributeNameState; void AfterAttributeNameState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.SOLIDUS)
                    @this.State = SELF_CLOSING_START_TAG_STATE;

                else if (cp == CP.EQUALS_SIGN)
                    @this.State = BEFORE_ATTRIBUTE_VALUE_STATE;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                {
                    @this.CreateAttr("");
                    @this.ReconsumeInState(ATTRIBUTE_NAME_STATE);
                }
            }

            // 12.2.4.37 Before attribute value state
            // ------------------------------------------------------------------
            _[BEFORE_ATTRIBUTE_VALUE_STATE] = BeforeAttributeValueState; void BeforeAttributeValueState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.QUOTATION_MARK)
                    @this.State = ATTRIBUTE_VALUE_DOUBLE_QUOTED_STATE;

                else if (cp == CP.APOSTROPHE)
                    @this.State = ATTRIBUTE_VALUE_SINGLE_QUOTED_STATE;

                else
                    @this.ReconsumeInState(ATTRIBUTE_VALUE_UNQUOTED_STATE);
            }

            // 12.2.4.38 Attribute value (double-quoted) state
            // ------------------------------------------------------------------
            _[ATTRIBUTE_VALUE_DOUBLE_QUOTED_STATE] = AttributeValueDoubleQuotedState; void AttributeValueDoubleQuotedState(Tokenizer @this, int cp)
            {
                if (cp == CP.QUOTATION_MARK)
                    @this.State = AFTER_ATTRIBUTE_VALUE_QUOTED_STATE;

                else if (cp == CP.AMPERSAND)
                {
                    @this.additionalAllowedCp = CP.QUOTATION_MARK;
                    @this.returnState = @this.State;
                    @this.State = CHARACTER_REFERENCE_IN_ATTRIBUTE_VALUE_STATE;
                }

                else if (cp == CP.NULL)
                    @this.currentAttrValue.Append((char) CP.REPLACEMENT_CHARACTER);

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.currentAttrValue.Append(ToChar(cp));
            }

            // 12.2.4.39 Attribute value (single-quoted) state
            // ------------------------------------------------------------------
            _[ATTRIBUTE_VALUE_SINGLE_QUOTED_STATE] = AttributeValueSingleQuotedState; void AttributeValueSingleQuotedState(Tokenizer @this, int cp)
            {
                if (cp == CP.APOSTROPHE)
                    @this.State = AFTER_ATTRIBUTE_VALUE_QUOTED_STATE;

                else if (cp == CP.AMPERSAND)
                {
                    @this.additionalAllowedCp = CP.APOSTROPHE;
                    @this.returnState = @this.State;
                    @this.State = CHARACTER_REFERENCE_IN_ATTRIBUTE_VALUE_STATE;
                }

                else if (cp == CP.NULL)
                    @this.currentAttrValue.Append((char) CP.REPLACEMENT_CHARACTER);

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.currentAttrValue.Append(ToChar(cp));
            }

            // 12.2.4.40 Attribute value (unquoted) state
            // ------------------------------------------------------------------
            _[ATTRIBUTE_VALUE_UNQUOTED_STATE] = AttributeValueUnquotedState; void AttributeValueUnquotedState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    @this.LeaveAttrValue(BEFORE_ATTRIBUTE_NAME_STATE);

                else if (cp == CP.AMPERSAND)
                {
                    @this.additionalAllowedCp = CP.GREATER_THAN_SIGN;
                    @this.returnState = @this.State;
                    @this.State = CHARACTER_REFERENCE_IN_ATTRIBUTE_VALUE_STATE;
                }

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.LeaveAttrValue(DATA_STATE);
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.NULL)
                    @this.currentAttrValue.Append((char) CP.REPLACEMENT_CHARACTER);

                else if (cp == CP.QUOTATION_MARK || cp == CP.APOSTROPHE || cp == CP.LESS_THAN_SIGN ||
                         cp == CP.EQUALS_SIGN || cp == CP.GRAVE_ACCENT)
                    @this.currentAttrValue.Append(ToChar(cp));

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.currentAttrValue.Append(ToChar(cp));
            }

            // 12.2.4.41 Character reference in attribute value state
            // ------------------------------------------------------------------
            _[CHARACTER_REFERENCE_IN_ATTRIBUTE_VALUE_STATE] = CharacterReferenceInAttributeValueState; void CharacterReferenceInAttributeValueState(Tokenizer @this, int cp)
            {
                var referencedCodePoints = @this.ConsumeCharacterReference(cp, true);

                if (!@this.EnsureHibernation())
                {
                    if (referencedCodePoints != null)
                    {
                        foreach (var rcp in referencedCodePoints)
                            @this.currentAttrValue.Append(ToChar(rcp));
                    }
                    else
                        @this.currentAttrValue.Append('&');

                    @this.State = @this.returnState;
                }
            }

            // 12.2.4.42 After attribute value (quoted) state
            // ------------------------------------------------------------------
            _[AFTER_ATTRIBUTE_VALUE_QUOTED_STATE] = AfterAttributeValueQuotedState; void AfterAttributeValueQuotedState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    @this.LeaveAttrValue(BEFORE_ATTRIBUTE_NAME_STATE);

                else if (cp == CP.SOLIDUS)
                    @this.LeaveAttrValue(SELF_CLOSING_START_TAG_STATE);

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.LeaveAttrValue(DATA_STATE);
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.ReconsumeInState(BEFORE_ATTRIBUTE_NAME_STATE);
            }

            // 12.2.4.43 Self-closing start tag state
            // ------------------------------------------------------------------
            _[SELF_CLOSING_START_TAG_STATE] = SelfClosingStartTagState; void SelfClosingStartTagState(Tokenizer @this, int cp)
            {
                if (cp == CP.GREATER_THAN_SIGN)
                {
                    if (@this.currentToken is StartTagToken startTagToken)
                        startTagToken.SelfClosing = true;
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.EOF)
                    @this.ReconsumeInState(DATA_STATE);

                else
                    @this.ReconsumeInState(BEFORE_ATTRIBUTE_NAME_STATE);
            }

            // 12.2.4.44 Bogus comment state
            // ------------------------------------------------------------------
            _[BOGUS_COMMENT_STATE] = BogusCommentState; void BogusCommentState(Tokenizer @this, int cp)
            {
                @this.CreateCommentToken();
                @this.ReconsumeInState(BOGUS_COMMENT_STATE_CONTINUATION);
            }

            // HACK: to support streaming and make BOGUS_COMMENT_STATE reentrant we've
            // introduced BOGUS_COMMENT_STATE_CONTINUATION state which will not produce
            // comment token on each call.
            _[BOGUS_COMMENT_STATE_CONTINUATION] = BogusCommentStateContinuation; void BogusCommentStateContinuation(Tokenizer @this, int cp)
            {
                while (true)
                {
                    if (cp == CP.GREATER_THAN_SIGN)
                    {
                        @this.State = DATA_STATE;
                        break;
                    }

                    else if (cp == CP.EOF)
                    {
                        @this.ReconsumeInState(DATA_STATE);
                        break;
                    }

                    else
                    {
                        @this.CurrentCommentToken.Data += (cp == CP.NULL ? ToChar(CP.REPLACEMENT_CHARACTER) : ToChar(cp));

                        @this.HibernationSnapshot();
                        cp = @this.Consume();

                        if (@this.EnsureHibernation())
                            return;
                    }
                }

                @this.EmitCurrentToken();
            }

            // 12.2.4.45 Markup declaration open state
            // ------------------------------------------------------------------
            _[MARKUP_DECLARATION_OPEN_STATE] = MarkupDeclarationOpenState; void MarkupDeclarationOpenState(Tokenizer @this, int cp)
            {
                var dashDashMatch = @this.ConsumeSubsequentIfMatch(CPS.DASH_DASH_STRING, cp, true);
                var doctypeMatch = !dashDashMatch && @this.ConsumeSubsequentIfMatch(CPS.DOCTYPE_STRING, cp, false);
                var cdataMatch = !dashDashMatch && !doctypeMatch &&
                             @this.AllowCData &&
                             @this.ConsumeSubsequentIfMatch(CPS.CDATA_START_STRING, cp, true);

                if (!@this.EnsureHibernation())
                {
                    if (dashDashMatch)
                    {
                        @this.CreateCommentToken();
                        @this.State = COMMENT_START_STATE;
                    }

                    else if (doctypeMatch)
                        @this.State = DOCTYPE_STATE;

                    else if (cdataMatch)
                        @this.State = CDATA_SECTION_STATE;

                    else
                        @this.ReconsumeInState(BOGUS_COMMENT_STATE);
                }
            }

            // 12.2.4.46 Comment start state
            // ------------------------------------------------------------------
            _[COMMENT_START_STATE] = CommentStartState; void CommentStartState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                    @this.State = COMMENT_START_DASH_STATE;

                else if (cp == CP.NULL)
                {
                    @this.CurrentCommentToken.Data += CP.REPLACEMENT_CHARACTER;
                    @this.State = COMMENT_STATE;
                }

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.EOF)
                {
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                {
                    @this.CurrentCommentToken.Data += ToChar(cp);
                    @this.State = COMMENT_STATE;
                }
            }

            // 12.2.4.47 Comment start dash state
            // ------------------------------------------------------------------
            _[COMMENT_START_DASH_STATE] = CommentStartDashState; void CommentStartDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                    @this.State = COMMENT_END_STATE;

                else if (cp == CP.NULL)
                {
                    @this.CurrentCommentToken.Data += '-';
                    @this.CurrentCommentToken.Data += CP.REPLACEMENT_CHARACTER;
                    @this.State = COMMENT_STATE;
                }

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.EOF)
                {
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                {
                    @this.CurrentCommentToken.Data += '-';
                    @this.CurrentCommentToken.Data += ToChar(cp);
                    @this.State = COMMENT_STATE;
                }
            }

            // 12.2.4.48 Comment state
            // ------------------------------------------------------------------
            _[COMMENT_STATE] = CommentState; void CommentState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                    @this.State = COMMENT_END_DASH_STATE;

                else if (cp == CP.NULL)
                    @this.CurrentCommentToken.Data += CP.REPLACEMENT_CHARACTER;

                else if (cp == CP.EOF)
                {
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                    @this.CurrentCommentToken.Data += ToChar(cp);
            }

            // 12.2.4.49 Comment end dash state
            // ------------------------------------------------------------------
            _[COMMENT_END_DASH_STATE] = CommentEndDashState; void CommentEndDashState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                    @this.State = COMMENT_END_STATE;

                else if (cp == CP.NULL)
                {
                    @this.CurrentCommentToken.Data += '-';
                    @this.CurrentCommentToken.Data += CP.REPLACEMENT_CHARACTER;
                    @this.State = COMMENT_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                {
                    @this.CurrentCommentToken.Data += '-';
                    @this.CurrentCommentToken.Data += ToChar(cp);
                    @this.State = COMMENT_STATE;
                }
            }

            // 12.2.4.50 Comment end state
            // ------------------------------------------------------------------
            _[COMMENT_END_STATE] = CommentEndState; void CommentEndState(Tokenizer @this, int cp)
            {
                if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.EXCLAMATION_MARK)
                    @this.State = COMMENT_END_BANG_STATE;

                else if (cp == CP.HYPHEN_MINUS)
                    @this.CurrentCommentToken.Data += '-';

                else if (cp == CP.NULL)
                {
                    @this.CurrentCommentToken.Data += "--";
                    @this.CurrentCommentToken.Data += CP.REPLACEMENT_CHARACTER;
                    @this.State = COMMENT_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.ReconsumeInState(DATA_STATE);
                    @this.EmitCurrentToken();
                }

                else
                {
                    @this.CurrentCommentToken.Data += "--";
                    @this.CurrentCommentToken.Data += ToChar(cp);
                    @this.State = COMMENT_STATE;
                }
            }

            // 12.2.4.51 Comment end bang state
            // ------------------------------------------------------------------
            _[COMMENT_END_BANG_STATE] = CommentEndBangState; void CommentEndBangState(Tokenizer @this, int cp)
            {
                if (cp == CP.HYPHEN_MINUS)
                {
                    @this.CurrentCommentToken.Data += "--!";
                    @this.State = COMMENT_END_DASH_STATE;
                }

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else if (cp == CP.NULL)
                {
                    @this.CurrentCommentToken.Data += "--!";
                    @this.CurrentCommentToken.Data += CP.REPLACEMENT_CHARACTER;
                    @this.State = COMMENT_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                {
                    @this.CurrentCommentToken.Data += "--!";
                    @this.CurrentCommentToken.Data += ToChar(cp);
                    @this.State = COMMENT_STATE;
                }
            }

            // 12.2.4.52 DOCTYPE state
            // ------------------------------------------------------------------
            _[DOCTYPE_STATE] = DoctypeState; void DoctypeState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.CreateDoctypeToken(null);
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.CreateDoctypeToken(null);
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }
                else
                {
                    @this.CreateDoctypeToken("");
                    @this.ReconsumeInState(DOCTYPE_NAME_STATE);
                }
            }

            // 12.2.4.54 DOCTYPE name state
            // ------------------------------------------------------------------
            _[DOCTYPE_NAME_STATE] = DoctypeNameState; void DoctypeNameState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp) || cp == CP.GREATER_THAN_SIGN || cp == CP.EOF)
                    @this.ReconsumeInState(AFTER_DOCTYPE_NAME_STATE);

                else if (IsAsciiUpper(cp))
                    @this.CurrentDoctypeToken.Name += ToAsciiLowerChar(cp);

                else if (cp == CP.NULL)
                    @this.CurrentDoctypeToken.Name += CP.REPLACEMENT_CHARACTER;

                else
                    @this.CurrentDoctypeToken.Name += ToChar(cp);
            }

            // 12.2.4.55 After DOCTYPE name state
            // ------------------------------------------------------------------
            _[AFTER_DOCTYPE_NAME_STATE] = AfterDoctypeNameState; void AfterDoctypeNameState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.State = DATA_STATE;
                    @this.EmitCurrentToken();
                }

                else
                {
                    var publicMatch = @this.ConsumeSubsequentIfMatch(CPS.PUBLIC_STRING, cp, false);
                    var systemMatch = !publicMatch && @this.ConsumeSubsequentIfMatch(CPS.SYSTEM_STRING, cp, false);

                    if (!@this.EnsureHibernation())
                    {
                        if (publicMatch)
                            @this.State = BEFORE_DOCTYPE_PUBLIC_IDENTIFIER_STATE;

                        else if (systemMatch)
                            @this.State = BEFORE_DOCTYPE_SYSTEM_IDENTIFIER_STATE;

                        else
                        {
                            @this.CurrentDoctypeToken.ForceQuirks = true;
                            @this.State = BOGUS_DOCTYPE_STATE;
                        }
                    }
                }
            }

            // 12.2.4.57 Before DOCTYPE public identifier state
            // ------------------------------------------------------------------
            _[BEFORE_DOCTYPE_PUBLIC_IDENTIFIER_STATE] = BeforeDoctypePublicIdentifierState; void BeforeDoctypePublicIdentifierState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.QUOTATION_MARK)
                {
                    @this.CurrentDoctypeToken.PublicId = "";
                    @this.State = DOCTYPE_PUBLIC_IDENTIFIER_DOUBLE_QUOTED_STATE;
                }

                else if (cp == CP.APOSTROPHE)
                {
                    @this.CurrentDoctypeToken.PublicId = "";
                    @this.State = DOCTYPE_PUBLIC_IDENTIFIER_SINGLE_QUOTED_STATE;
                }

                else
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.ReconsumeInState(BOGUS_DOCTYPE_STATE);
                }
            }

            // 12.2.4.58 DOCTYPE public identifier (double-quoted) state
            // ------------------------------------------------------------------
            _[DOCTYPE_PUBLIC_IDENTIFIER_DOUBLE_QUOTED_STATE] = DoctypePublicIdentifierDoubleQuotedState; void DoctypePublicIdentifierDoubleQuotedState(Tokenizer @this, int cp)
            {
                if (cp == CP.QUOTATION_MARK)
                    @this.State = BETWEEN_DOCTYPE_PUBLIC_AND_SYSTEM_IDENTIFIERS_STATE;

                else if (cp == CP.NULL)
                    @this.CurrentDoctypeToken.PublicId += CP.REPLACEMENT_CHARACTER;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                    @this.CurrentDoctypeToken.PublicId += ToChar(cp);
            }

            // 12.2.4.59 DOCTYPE public identifier (single-quoted) state
            // ------------------------------------------------------------------
            _[DOCTYPE_PUBLIC_IDENTIFIER_SINGLE_QUOTED_STATE] = DoctypePublicIdentifierSingleQuotedState; void DoctypePublicIdentifierSingleQuotedState(Tokenizer @this, int cp)
            {
                if (cp == CP.APOSTROPHE)
                    @this.State = BETWEEN_DOCTYPE_PUBLIC_AND_SYSTEM_IDENTIFIERS_STATE;

                else if (cp == CP.NULL)
                    @this.CurrentDoctypeToken.PublicId += CP.REPLACEMENT_CHARACTER;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                    @this.CurrentDoctypeToken.PublicId += ToChar(cp);
            }

            // 12.2.4.61 Between DOCTYPE public and system identifiers state
            // ------------------------------------------------------------------
            _[BETWEEN_DOCTYPE_PUBLIC_AND_SYSTEM_IDENTIFIERS_STATE] = BetweenDoctypePublicAndSystemIdentifiersState; void BetweenDoctypePublicAndSystemIdentifiersState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.QUOTATION_MARK)
                {
                    @this.CurrentDoctypeToken.SystemId = "";
                    @this.State = DOCTYPE_SYSTEM_IDENTIFIER_DOUBLE_QUOTED_STATE;
                }

                else if (cp == CP.APOSTROPHE)
                {
                    @this.CurrentDoctypeToken.SystemId = "";
                    @this.State = DOCTYPE_SYSTEM_IDENTIFIER_SINGLE_QUOTED_STATE;
                }

                else
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.ReconsumeInState(BOGUS_DOCTYPE_STATE);
                }
            }

            // 12.2.4.63 Before DOCTYPE system identifier state
            // ------------------------------------------------------------------
            _[BEFORE_DOCTYPE_SYSTEM_IDENTIFIER_STATE] = BeforeDoctypeSystemIdentifierState; void BeforeDoctypeSystemIdentifierState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.QUOTATION_MARK)
                {
                    @this.CurrentDoctypeToken.SystemId = "";
                    @this.State = DOCTYPE_SYSTEM_IDENTIFIER_DOUBLE_QUOTED_STATE;
                }

                else if (cp == CP.APOSTROPHE)
                {
                    @this.CurrentDoctypeToken.SystemId = "";
                    @this.State = DOCTYPE_SYSTEM_IDENTIFIER_SINGLE_QUOTED_STATE;
                }

                else
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.ReconsumeInState(BOGUS_DOCTYPE_STATE);
                }
            }

            // 12.2.4.64 DOCTYPE system identifier (double-quoted) state
            // ------------------------------------------------------------------
            _[DOCTYPE_SYSTEM_IDENTIFIER_DOUBLE_QUOTED_STATE] = DoctypeSystemIdentifierDoubleQuotedState; void DoctypeSystemIdentifierDoubleQuotedState(Tokenizer @this, int cp)
            {
                if (cp == CP.QUOTATION_MARK)
                    @this.State = AFTER_DOCTYPE_SYSTEM_IDENTIFIER_STATE;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.NULL)
                    @this.CurrentDoctypeToken.SystemId += CP.REPLACEMENT_CHARACTER;

                else if (cp == CP.EOF)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                    @this.CurrentDoctypeToken.SystemId += ToChar(cp);
            }

            // 12.2.4.65 DOCTYPE system identifier (single-quoted) state
            // ------------------------------------------------------------------
            _[DOCTYPE_SYSTEM_IDENTIFIER_SINGLE_QUOTED_STATE] = DoctypeSystemIdentifierSingleQuotedState; void DoctypeSystemIdentifierSingleQuotedState(Tokenizer @this, int cp)
            {
                if (cp == CP.APOSTROPHE)
                    @this.State = AFTER_DOCTYPE_SYSTEM_IDENTIFIER_STATE;

                else if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.NULL)
                    @this.CurrentDoctypeToken.SystemId += CP.REPLACEMENT_CHARACTER;

                else if (cp == CP.EOF)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                    @this.CurrentDoctypeToken.SystemId += ToChar(cp);
            }

            // 12.2.4.66 After DOCTYPE system identifier state
            // ------------------------------------------------------------------
            _[AFTER_DOCTYPE_SYSTEM_IDENTIFIER_STATE] = AfterDoctypeSystemIdentifierState; void AfterDoctypeSystemIdentifierState(Tokenizer @this, int cp)
            {
                if (IsWhitespace(cp))
                    return;

                if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.CurrentDoctypeToken.ForceQuirks = true;
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }

                else
                    @this.State = BOGUS_DOCTYPE_STATE;
            }

            // 12.2.4.67 Bogus DOCTYPE state
            // ------------------------------------------------------------------
            _[BOGUS_DOCTYPE_STATE] = BogusDoctypeState; void BogusDoctypeState(Tokenizer @this, int cp)
            {
                if (cp == CP.GREATER_THAN_SIGN)
                {
                    @this.EmitCurrentToken();
                    @this.State = DATA_STATE;
                }

                else if (cp == CP.EOF)
                {
                    @this.EmitCurrentToken();
                    @this.ReconsumeInState(DATA_STATE);
                }
            }

            // 12.2.4.68 CDATA section state
            // ------------------------------------------------------------------
            _[CDATA_SECTION_STATE] = CDataSectionState; void CDataSectionState(Tokenizer @this, int cp)
            {
                while (true)
                {
                    if (cp == CP.EOF)
                    {
                        @this.ReconsumeInState(DATA_STATE);
                        break;
                    }

                    else
                    {
                        var cdataEndMatch = @this.ConsumeSubsequentIfMatch(CPS.CDATA_END_STRING, cp, true);

                        if (@this.EnsureHibernation())
                            break;

                        if (cdataEndMatch)
                        {
                            @this.State = DATA_STATE;
                            break;
                        }

                        @this.EmitCodePoint(cp);

                        @this.HibernationSnapshot();
                        cp = @this.Consume();

                        if (@this.EnsureHibernation())
                            break;
                    }
                }
            }

            return _;
        }
    }
}
