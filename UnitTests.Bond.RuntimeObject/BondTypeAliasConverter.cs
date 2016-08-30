namespace Bond.RuntimeObject.UnitTests
{
    using System;

    public static class BondTypeAliasConverter
    {
        public static long Convert(DateTime value, long unused)
        {
            return value.Ticks;
        }

        public static DateTime Convert(long value, DateTime unused)
        {
            if (value >= DateTime.MinValue.Ticks && value <= DateTime.MaxValue.Ticks)
                return new DateTime(value);

            return default(DateTime);
        }
    }
}
