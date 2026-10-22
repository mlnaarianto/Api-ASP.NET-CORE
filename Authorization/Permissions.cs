namespace ApiBe.Authorization;

// Satu tempat untuk semua nama permission.
// Kenapa pakai konstanta, bukan string langsung di controller?
// Kalau salah ketik ("user.view" vs "users.view"), compiler langsung error.
// Kalau pakai string biasa, salah ketik baru ketahuan saat runtime (selalu 403).
public static class Permissions
{
    // Nama tipe claim di JWT dan di tabel AspNetRoleClaims/AspNetUserClaims.
    // Semua permission memakai tipe yang sama, bedanya hanya di value-nya.
    public const string Type = "permission";

    // Dikelompokkan per resource. Format "resource.aksi" mirip konvensi Spatie.
    public static class Users
    {
        public const string View   = "users.view";
        public const string Create = "users.create";
        public const string Update = "users.update";
        public const string Delete = "users.delete";
    }

    public static class Roles
    {
        public const string View   = "roles.view";
        public const string Manage = "roles.manage";
    }

    // Daftar semua permission dalam satu list.
    // Berguna nanti untuk seeder (membuat/memberi permission) dan endpoint "daftar permission".
    // Dibuat lewat reflection: setiap const string di dalam nested class ikut terambil,
    // jadi kalau kamu menambah permission baru di atas, daftar ini otomatis ikut.
    public static IReadOnlyList<string> All { get; } = typeof(Permissions)
        .GetNestedTypes()
        .SelectMany(t => t.GetFields())
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}