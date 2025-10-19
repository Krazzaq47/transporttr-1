namespace MT.Controllers
{
    public sealed class TwilioVerifyOptions
    {
        public string AccountSid { get; set; } = "";
        public string AuthToken { get; set; } = "";
        public string VerifyServiceSid { get; set; } = "";
        public int DefaultTtlSeconds { get; set; } = 300;
    }

    //public static class TwilioVerifyExtensions
    //{
    //    public static IServiceCollection AddTwilioVerify(this IServiceCollection services, IConfiguration cfg)
    //    {
    //        services.Configure<TwilioVerifyOptions>(cfg.GetSection("Twilio"));
    //        services.AddSingleton(sp =>
    //        {
    //            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwilioVerifyOptions>>().Value;
    //            Twilio.TwilioClient.Init(o.AccountSid, o.AuthToken);
    //            return o;
    //        });
    //        return services;
    //    }
    //}

    public static class TwilioVerifyExtensions
    {
        public static IServiceCollection AddTwilioVerify(this IServiceCollection services, IConfiguration cfg)
        {
            var section = cfg.GetSection("Twilio");
            var opts = section.Get<TwilioVerifyOptions>() ?? new TwilioVerifyOptions();

            // initialize Twilio globally, right now
            Twilio.TwilioClient.Init(opts.AccountSid, opts.AuthToken);

            services.Configure<TwilioVerifyOptions>(section);
            services.AddSingleton(opts);

            return services;
        }
    }


}
