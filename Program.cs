using System.Text;
using ApiBe.Authorization;                        // BARU: untuk PermissionPolicyProvider & PermissionHandler
using ApiBe.Data;
using ApiBe.Models;
using ApiBe.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;         // BARU: untuk IAuthorizationPolicyProvider & IAuthorizationHandler
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;   // OpenApi 2.x: tipe Swagger ada di namespace ini (bukan lagi .Models)

// Builder = tempat mendaftarkan semua service (Dependency Injection) sebelum app dibuat.
var builder = WebApplication.CreateBuilder(args);

// ---------- Database ----------
// Mendaftarkan DbContext ke DI dan memakai PostgreSQL (Npgsql).
// Connection string dibaca dari "ConnectionStrings:DefaultConnection"
// (di Docker diisi lewat environment variable ConnectionStrings__DefaultConnection).
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------- Identity ----------
// AddIdentityCore = versi ringan Identity (tanpa UI dan cookie), cocok untuk API.
// Menyediakan UserManager untuk membuat user, cek password, dan mengelola role.
builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;   // dua user tidak boleh memakai email yang sama
    })
    .AddRoles<ApplicationRole>()                    // mengaktifkan fitur role
    .AddEntityFrameworkStores<AppDbContext>();      // data user/role disimpan lewat EF Core

// ---------- JWT ----------
// Mengikat bagian "Jwt" di konfigurasi ke class JwtOptions,
// lalu TokenService bisa menerimanya lewat IOptions<JwtOptions>.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
// Scoped = satu instance per request HTTP.
builder.Services.AddScoped<ITokenService, TokenService>();

// Membaca konfigurasi Jwt sekarang juga (bukan lewat DI) karena nilainya dibutuhkan
// langsung di bawah untuk memasang validasi token.
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Konfigurasi Jwt tidak ditemukan");
// HS256 butuh key minimal 256 bit (32 karakter). Lebih baik gagal saat start
// daripada berjalan dengan key yang lemah.
if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key minimal 32 karakter");

// ---------- Authentication ----------
// Menetapkan JWT Bearer sebagai cara default memeriksa "siapa kamu" di setiap request.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Aturan yang dicek ASP.NET Core pada setiap access token yang masuk.
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,               // token harus dari penerbit yang kita kenal
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,             // token harus ditujukan untuk API ini
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,     // tanda tangan harus cocok (token tidak dipalsukan)
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,             // token kedaluwarsa otomatis ditolak
            ClockSkew = TimeSpan.FromSeconds(30) // toleransi selisih jam server (default 5 menit)
        };
    });

// ---------- Authorization (permission) ----------   // BARU
// Provider: membuat policy otomatis dari nama permission di [HasPermission("...")].
// Handler: memeriksa apakah user punya permission tersebut (atau berperan Admin).
// Singleton karena keduanya tidak menyimpan data per request.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();   // BARU
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();                 // BARU

// ---------- Format error ----------
// Mendaftarkan penulis ProblemDetails (RFC 7807). Dipakai oleh UseExceptionHandler
// dan UseStatusCodePages di bawah supaya semua error berbentuk JSON yang sama:
// { type, title, status, traceId }.
builder.Services.AddProblemDetails();

// ---------- Controller & Swagger ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();   // menyediakan metadata endpoint untuk Swagger

builder.Services.AddSwaggerGen(o =>
{
    // Mendefinisikan skema "Bearer" supaya Swagger UI menampilkan tombol Authorize.
    // Scheme = "bearer" membuat Swagger menambahkan awalan "Bearer " otomatis,
    // jadi saat Authorize cukup paste accessToken saja.
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste accessToken dari endpoint login (tanpa kata 'Bearer')"
    });

    // Menerapkan skema di atas ke semua endpoint. Header Authorization hanya dikirim
    // kalau token sudah diisi lewat tombol Authorize.
    // Di OpenApi 2.x, referensi dibuat lewat OpenApiSecuritySchemeReference dan
    // butuh dokumen (doc) sebagai parameter, bukan lagi OpenApiReference.
    // "Bearer" harus sama dengan nama di AddSecurityDefinition.
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = []   // [] = tanpa scope (scope untuk OAuth)
    });
});

// Sampai sini tahap "mendaftarkan service" selesai. Build() membuat aplikasinya.
var app = builder.Build();

// ---------- Migrasi & seeder ----------
// Jalan sekali saat aplikasi start. Scope dibuat manual karena DbContext bersifat
// Scoped dan di luar request HTTP tidak ada scope otomatis.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();   // menerapkan migrasi EF yang belum dijalankan
}
// Mengisi data awal (misal user admin dan role). Kalau gagal, app berhenti,
// dan itu memang diinginkan.
await DbSeeder.SeedAsync(app.Services);

// ---------- Middleware pipeline ----------
// Urutan penting: request mengalir dari atas ke bawah, response dari bawah ke atas.

// Menangkap exception yang tidak tertangani dan mengubahnya jadi 500 ProblemDetails
// tanpa stack trace. Harus paling atas agar bisa menangkap error dari semua middleware di bawahnya.
app.UseExceptionHandler();

// Mengisi body ProblemDetails untuk response 4xx/5xx yang kosong
// (misal 401 dari [Authorize] atau 404 untuk route yang tidak ada).
app.UseStatusCodePages();

// Swagger hanya aktif di Development supaya dokumentasi API tidak terbuka di production.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();       // menghasilkan file swagger.json
    app.UseSwaggerUI();     // halaman web untuk mencoba API di /swagger
}

app.UseAuthentication();   // 1) baca token dan tentukan siapa user-nya
app.UseAuthorization();    // 2) cek apakah user boleh mengakses ([Authorize], [HasPermission], dll.)
                           //    harus SETELAH UseAuthentication
app.MapControllers();      // menghubungkan URL ke method di controller

app.Run();