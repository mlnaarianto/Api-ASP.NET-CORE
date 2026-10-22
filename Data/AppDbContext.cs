using ApiBe.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApiBe.Data;

// AppDbContext = jembatan antara kode C# dan database PostgreSQL.
// Mewarisi IdentityDbContext supaya tabel autentikasi bawaan ikut dibuat:
// AspNetUsers, AspNetRoles, AspNetUserRoles, dst.
// Tipe <ApplicationUser, ApplicationRole, Guid> artinya:
//   - user memakai kelas ApplicationUser
//   - role memakai kelas ApplicationRole
//   - primary key keduanya bertipe Guid (UUID)
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    // DbSet = satu tabel. Tabel RefreshTokens menyimpan refresh token (hash-nya)
    // yang dipakai untuk memperbarui sesi login.
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Tempat mengatur skema tabel secara manual (seperti isi file migration di Laravel).
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Wajib dipanggil pertama, supaya konfigurasi tabel Identity bawaan tetap berlaku.
        base.OnModelCreating(builder);

        // Konfigurasi tabel AspNetUsers (ApplicationUser)
        builder.Entity<ApplicationUser>(e =>
        {
            // Batasi panjang kolom: jadi varchar(50) dan varchar(100) di PostgreSQL,
            // bukan text tanpa batas.
            e.Property(u => u.UserName).HasMaxLength(50);
            e.Property(u => u.FullName).HasMaxLength(100);
        });

        // Konfigurasi tabel RefreshTokens
        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();

            // WithMany(u => u.RefreshTokens) menyatukan relasi dengan properti di ApplicationUser,
            // sehingga EF tidak membuat kolom ApplicationUserId tambahan.
            e.HasOne(t => t.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}