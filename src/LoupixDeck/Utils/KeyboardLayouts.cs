using LoupixDeck.Models;

namespace LoupixDeck.Utils;

public static class KeyboardLayouts
{
    private static KeyboardLayout Us { get; } = new KeyboardLayout("us", new Dictionary<char, (int keycode, bool shift)>
    {
        [' '] = (57, false),
        ['!'] = (2, true),
        ['"'] = (40, true),
        ['#'] = (4, true),
        ['$'] = (5, true),
        ['%'] = (6, true),
        ['&'] = (8, true),
        ['\''] = (40, false),
        ['('] = (10, true),
        [')'] = (11, true),
        ['*'] = (9, true),
        ['+'] = (13, true),
        [','] = (51, false),
        ['-'] = (12, false),
        ['.'] = (52, false),
        ['/'] = (53, false),
        ['0'] = (11, false),
        ['1'] = (2, false),
        ['2'] = (3, false),
        ['3'] = (4, false),
        ['4'] = (5, false),
        ['5'] = (6, false),
        ['6'] = (7, false),
        ['7'] = (8, false),
        ['8'] = (9, false),
        ['9'] = (10, false),
        [':'] = (39, true),
        [';'] = (39, false),
        ['<'] = (51, true),
        ['='] = (13, false),
        ['>'] = (52, true),
        ['?'] = (53, true),
        ['@'] = (3, true),
        ['A'] = (30, true),
        ['B'] = (48, true),
        ['C'] = (46, true),
        ['D'] = (32, true),
        ['E'] = (18, true),
        ['F'] = (33, true),
        ['G'] = (34, true),
        ['H'] = (35, true),
        ['I'] = (23, true),
        ['J'] = (36, true),
        ['K'] = (37, true),
        ['L'] = (38, true),
        ['M'] = (50, true),
        ['N'] = (49, true),
        ['O'] = (24, true),
        ['P'] = (25, true),
        ['Q'] = (16, true),
        ['R'] = (19, true),
        ['S'] = (31, true),
        ['T'] = (20, true),
        ['U'] = (22, true),
        ['V'] = (47, true),
        ['W'] = (17, true),
        ['X'] = (45, true),
        ['Y'] = (21, true),
        ['Z'] = (44, true),
        ['['] = (26, false),
        ['\\'] = (43, false),
        [']'] = (27, false),
        ['^'] = (7, true),
        ['_'] = (12, true),
        ['`'] = (41, false),
        ['a'] = (30, false),
        ['b'] = (48, false),
        ['c'] = (46, false),
        ['d'] = (32, false),
        ['e'] = (18, false),
        ['f'] = (33, false),
        ['g'] = (34, false),
        ['h'] = (35, false),
        ['i'] = (23, false),
        ['j'] = (36, false),
        ['k'] = (37, false),
        ['l'] = (38, false),
        ['m'] = (50, false),
        ['n'] = (49, false),
        ['o'] = (24, false),
        ['p'] = (25, false),
        ['q'] = (16, false),
        ['r'] = (19, false),
        ['s'] = (31, false),
        ['t'] = (20, false),
        ['u'] = (22, false),
        ['v'] = (47, false),
        ['w'] = (17, false),
        ['x'] = (45, false),
        ['y'] = (21, false),
        ['z'] = (44, false),
        ['{'] = (26, true),
        ['|'] = (43, true),
        ['}'] = (27, true),
        ['~'] = (41, true)
    });

    private static KeyboardLayout De { get; } = new KeyboardLayout("de", new Dictionary<char, (int keycode, bool shift)>
    {
        [' '] = (57, false),

        ['1'] = (2, false),
        ['!'] = (2, true),

        ['2'] = (3, false),
        ['"'] = (3, true),

        ['3'] = (4, false),
        ['§'] = (4, true),

        ['4'] = (5, false),
        ['$'] = (5, true),

        ['5'] = (6, false),
        ['%'] = (6, true),

        ['6'] = (7, false),
        ['&'] = (7, true),

        ['7'] = (8, false),
        ['/'] = (8, true),

        ['8'] = (9, false),
        ['('] = (9, true),

        ['9'] = (10, false),
        [')'] = (10, true),

        ['0'] = (11, false),
        ['='] = (11, true),

        ['ß'] = (12, false),
        ['?'] = (12, true),
        
        ['´'] = (13, false),
        ['`'] = (13, true),

        ['q'] = (16, false),
        ['Q'] = (16, true),

        ['w'] = (17, false),
        ['W'] = (17, true),

        ['e'] = (18, false),
        ['E'] = (18, true),

        ['r'] = (19, false),
        ['R'] = (19, true),

        ['t'] = (20, false),
        ['T'] = (20, true),

        ['z'] = (21, false),
        ['Z'] = (21, true),

        ['u'] = (22, false),
        ['U'] = (22, true),

        ['i'] = (23, false),
        ['I'] = (23, true),

        ['o'] = (24, false),
        ['O'] = (24, true),

        ['p'] = (25, false),
        ['P'] = (25, true),

        ['ü'] = (26, false),
        ['Ü'] = (26, true),

        ['+'] = (27, false),
        ['*'] = (27, true),

        ['a'] = (30, false),
        ['A'] = (30, true),

        ['s'] = (31, false),
        ['S'] = (31, true),

        ['d'] = (32, false),
        ['D'] = (32, true),

        ['f'] = (33, false),
        ['F'] = (33, true),

        ['g'] = (34, false),
        ['G'] = (34, true),

        ['h'] = (35, false),
        ['H'] = (35, true),

        ['j'] = (36, false),
        ['J'] = (36, true),

        ['k'] = (37, false),
        ['K'] = (37, true),

        ['l'] = (38, false),
        ['L'] = (38, true),

        ['ö'] = (39, false),
        ['Ö'] = (39, true),

        ['ä'] = (40, false),
        ['Ä'] = (40, true),

        ['#'] = (43, false),
        ['\''] = (43, true),

        ['y'] = (44, false),
        ['Y'] = (44, true),

        ['x'] = (45, false),
        ['X'] = (45, true),

        ['c'] = (46, false),
        ['C'] = (46, true),

        ['v'] = (47, false),
        ['V'] = (47, true),

        ['b'] = (48, false),
        ['B'] = (48, true),

        ['n'] = (49, false),
        ['N'] = (49, true),

        ['m'] = (50, false),
        ['M'] = (50, true),

        [','] = (51, false),
        [';'] = (51, true),

        ['.'] = (52, false),
        [':'] = (52, true),

        ['-'] = (53, false),
        ['_'] = (53, true)
    });

    public static KeyboardLayout GetLayout(string locale)
    {
        return locale.ToLower() switch
        {
            "us" => Us,
            "de" => De,
            _ => Us
        };
    }
}