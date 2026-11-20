using Ash.Infrastructure.Persistence;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>Live search: the one rule every server-side text search uses (TextSearch).</summary>
public class TextSearchTests
{
    [Theory]
    [InlineData("5", "%5%")]
    [InlineData("2026-12", "%2026-12%")]
    [InlineData("50%", @"%50\%%")]
    [InlineData("A_B", @"%A\_B%")]
    [InlineData(@"a\b", @"%a\\b%")]
    public void LIKE_wildcards_and_the_escape_are_literal(string term, string pattern) =>
        TextSearch.LikePattern(term).ShouldBe(pattern);

    [Fact]
    public void A_text_is_split_on_whitespace_into_distinct_terms()
    {
        var s = TextSearch.Parse("  Hoxha   hoxha  2026-12 ");
        s.Text.ShouldBe("Hoxha   hoxha  2026-12");
        s.Terms.ShouldBe(["Hoxha", "2026-12"]);
        s.IsEmpty.ShouldBeFalse();
        TextSearch.Parse("   ").IsEmpty.ShouldBeTrue();
        TextSearch.Parse(null).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void One_character_terms_are_dropped_so_a_text_of_them_only_is_no_search()
    {
        TextSearch.Parse("5").IsEmpty.ShouldBeTrue();
        TextSearch.Parse("a b %").IsEmpty.ShouldBeTrue();
        var s = TextSearch.Parse("e Hoxha 5");
        s.Terms.ShouldBe(["Hoxha"]);
        s.Words.ShouldBe("+Hoxha");
    }

    [Fact]
    public void Two_symbols_have_no_ngram_words_so_the_LIKE_alone_decides()
    {
        var s = TextSearch.Parse("0%");
        s.Words.ShouldBe(string.Empty);
        s.Predicate("q", "a.case_code").ShouldBe("(a.case_code LIKE @q_l0)");
    }

    [Fact]
    public void Two_characters_narrow_on_the_index_and_the_LIKE_decides()
    {
        var s = TextSearch.Parse("2026-12 ab");
        s.Words.ShouldBe("+2026 +12 +ab");
        s.Predicate("q", "c1", "c2").ShouldBe(
            "MATCH(c1, c2) AGAINST (@q_w IN BOOLEAN MODE) AND (c1 LIKE @q_l0 OR c2 LIKE @q_l0) AND (c1 LIKE @q_l1 OR c2 LIKE @q_l1)");
        s.Parameters("q").Select(p => (p.ParameterName, p.Value)).ShouldBe(
            [("@q_w", (object)"+2026 +12 +ab"), ("@q_l0", "%2026-12%"), ("@q_l1", "%ab%")]);
    }

    [Theory]
    [InlineData("+hoxha -arben", "+hoxha +arben")]
    [InlineData("\"ab cd\" (ef) ~gh <ij> @kl mn*", "+ab +cd +ef +gh +ij +kl +mn")]
    [InlineData("<script>", "+script")]
    public void FULLTEXT_boolean_operators_never_reach_the_index(string q, string words) =>
        TextSearch.Parse(q).Words.ShouldBe(words);

    [Fact]
    public void Inline_literals_are_quoted_and_escaped()
    {
        TextSearch.Literal("O'Brien").ShouldBe(@"'O\'Brien'");
        TextSearch.Literal(@"a\'b").ShouldBe(@"'a\\\'b'");
        TextSearch.Parse("O'B").InlinePredicate("x").ShouldBe(@"(x LIKE '%O\'B%')");
        TextSearch.Parse(string.Empty).InlinePredicate("x").ShouldBe("FALSE");
    }
}
