namespace SqlVisualiserWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();

            // Add services to the container
            builder.Services.AddControllersWithViews();

            // Register application services
            builder.Services.AddScoped<Services.SqlVisualiserService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts(); // Enforce HTTPS in production
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            // Configure default route
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=SqlVisualiser}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
