using System.Collections.Generic;
using System.Linq;

namespace Paraparty.Strings
{
    public abstract class PropertyNamingStrategy
    {
        public static readonly IReadOnlyCollection<string> DefaultLockedWords = new List<string> { };

        public static PropertyNamingStrategy UpperCamel { get; } = new UpperCamelNamingStrategy(DefaultLockedWords);

        public static PropertyNamingStrategy LowerCamel { get; } = new LowerCamelNamingStrategy(DefaultLockedWords);

        public static PropertyNamingStrategy UpperSnake { get; } = new UpperSnakeNamingStrategy(DefaultLockedWords);

        public static PropertyNamingStrategy LowerSnake { get; } = new LowerSnakeNamingStrategy(DefaultLockedWords);

        public static PropertyNamingStrategy UpperKebab { get; } = new UpperKebabNamingStrategy(DefaultLockedWords);

        public static PropertyNamingStrategy LowerKebab { get; } = new LowerKebabNamingStrategy(DefaultLockedWords);

        public abstract string Convert(string src);

        public abstract class SimplePropertyNamingStrategy : PropertyNamingStrategy
        {
            protected readonly string[] LockedWords;

            public string[] GetLockedWords()
            {
                return LockedWords.ToArray();
            }

            protected SimplePropertyNamingStrategy(IEnumerable<string> lockedWords)
            {
                LockedWords = lockedWords.ToArray();
            }
        }

        public class UpperCamelNamingStrategy : SimplePropertyNamingStrategy
        {
            public override string Convert(string src)
                => NamingStrategyUtil.ToCamelCase(src, NamingStrategyUtil.CamelCaseType.Upper, LockedWords);

            public UpperCamelNamingStrategy(IEnumerable<string> lockedWords) : base(lockedWords)
            {
            }
        }

        public class LowerCamelNamingStrategy : SimplePropertyNamingStrategy
        {
            public override string Convert(string src)
                => NamingStrategyUtil.ToCamelCase(src, NamingStrategyUtil.CamelCaseType.Lower, LockedWords);

            public LowerCamelNamingStrategy(IEnumerable<string> lockedWords) : base(lockedWords)
            {
            }
        }

        public class UpperSnakeNamingStrategy : SimplePropertyNamingStrategy
        {
            public override string Convert(string src)
                => NamingStrategyUtil.ToSeparatedCase(src, NamingStrategyUtil.Underline, NamingStrategyUtil.CaseType.Upper, LockedWords);

            public UpperSnakeNamingStrategy(IEnumerable<string> lockedWords) : base(lockedWords)
            {
            }
        }

        public class LowerSnakeNamingStrategy : SimplePropertyNamingStrategy
        {
            public override string Convert(string src)
                => NamingStrategyUtil.ToSeparatedCase(src, NamingStrategyUtil.Underline, NamingStrategyUtil.CaseType.Lower, LockedWords);

            public LowerSnakeNamingStrategy(IEnumerable<string> lockedWords) : base(lockedWords)
            {
            }
        }

        public class UpperKebabNamingStrategy : SimplePropertyNamingStrategy
        {
            public override string Convert(string src)
                => NamingStrategyUtil.ToSeparatedCase(src, NamingStrategyUtil.Dash, NamingStrategyUtil.CaseType.Upper, LockedWords);

            public UpperKebabNamingStrategy(IEnumerable<string> lockedWords) : base(lockedWords)
            {
            }
        }

        public class LowerKebabNamingStrategy : SimplePropertyNamingStrategy
        {
            public override string Convert(string src)
                => NamingStrategyUtil.ToSeparatedCase(src, NamingStrategyUtil.Dash, NamingStrategyUtil.CaseType.Lower, LockedWords);

            public LowerKebabNamingStrategy(IEnumerable<string> lockedWords) : base(lockedWords)
            {
            }
        }
    }
}
