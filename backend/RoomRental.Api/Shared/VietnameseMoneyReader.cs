namespace RoomRental.Api.Services;

public static class VietnameseMoneyReader
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Units = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    public static string ToVietnameseWords(decimal amount)
    {
        var value = (long)Math.Round(Math.Max(0, amount), 0, MidpointRounding.AwayFromZero);
        if (value == 0)
            return "Không đồng";

        var groups = new List<int>();
        var remaining = value;
        while (remaining > 0)
        {
            groups.Add((int)(remaining % 1000));
            remaining /= 1000;
        }

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (group == 0)
                continue;

            var isLeadingGroup = i == groups.Count - 1;
            var groupText = ReadThreeDigits(group, !isLeadingGroup);
            var unit = i < Units.Length ? Units[i] : "";
            parts.Add(string.IsNullOrEmpty(unit) ? groupText : $"{groupText} {unit}");
        }

        var sentence = string.Join(" ", parts).Trim() + " đồng chẵn";
        return char.ToUpper(sentence[0]) + sentence[1..];
    }

    private static string ReadThreeDigits(int number, bool readFullHundreds)
    {
        var hundreds = number / 100;
        var tens = (number % 100) / 10;
        var ones = number % 10;
        var words = new List<string>();

        if (hundreds > 0 || readFullHundreds)
        {
            words.Add($"{Digits[hundreds]} trăm");
        }

        if (tens > 1)
        {
            words.Add($"{Digits[tens]} mươi");
            if (ones == 1)
                words.Add("mốt");
            else if (ones == 4)
                words.Add("tư");
            else if (ones == 5)
                words.Add("lăm");
            else if (ones > 0)
                words.Add(Digits[ones]);
        }
        else if (tens == 1)
        {
            words.Add("mười");
            if (ones == 5)
                words.Add("lăm");
            else if (ones > 0)
                words.Add(Digits[ones]);
        }
        else if (ones > 0)
        {
            if (hundreds > 0 || readFullHundreds)
                words.Add("lẻ");
            words.Add(Digits[ones]);
        }

        return string.Join(" ", words);
    }
}
