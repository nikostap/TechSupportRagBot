using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Data;

/// <summary>
/// Главный контекст базы данных приложения.
/// 
/// Через этот класс Entity Framework работает с таблицами:
/// пользователи, станки, документы RAG, чанки RAG.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    /// <summary>
    /// Конструктор контекста базы данных.
    /// 
    /// Параметры подключения передаются из Program.cs.
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Таблица компаний-клиентов.
    /// </summary>
    public DbSet<Client> Clients => Set<Client>();

    /// <summary>
    /// Таблица лицензий.
    /// </summary>
    public DbSet<License> Licenses => Set<License>();

    /// <summary>
    /// Таблица доступов клиентов к станкам.
    /// </summary>
    public DbSet<ClientMachine> ClientMachines => Set<ClientMachine>();

    /// <summary>
    /// Таблица обращений в поддержку.
    /// </summary>
    public DbSet<Ticket> Tickets => Set<Ticket>();

    /// <summary>
    /// Таблица сообщений в обращениях.
    /// </summary>
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    /// <summary>
    /// Таблица вложений к сообщениям.
    /// </summary>
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<ChatMessageTranslation> ChatMessageTranslations => Set<ChatMessageTranslation>();

    /// <summary>
    /// Таблица решенных ответов для будущего RAG.
    /// </summary>
    public DbSet<ResolvedAnswer> ResolvedAnswers => Set<ResolvedAnswer>();

    /// <summary>
    /// Таблица станков.
    /// </summary>
    public DbSet<Machine> Machines => Set<Machine>();

    /// <summary>
    /// Таблица загруженных документов базы знаний.
    /// </summary>
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    /// <summary>
    /// Таблица смысловых чанков документов.
    /// </summary>
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();

    public DbSet<TicketOperatorAssignment> TicketOperatorAssignments => Set<TicketOperatorAssignment>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<QAEntry> QAEntries => Set<QAEntry>();

    public DbSet<QAAttachment> QAAttachments => Set<QAAttachment>();

    public DbSet<OperatorChatPresence> OperatorChatPresences => Set<OperatorChatPresence>();

    public DbSet<OperatorChatTimeEntry> OperatorChatTimeEntries => Set<OperatorChatTimeEntry>();

    public DbSet<EmailNotificationLog> EmailNotificationLogs => Set<EmailNotificationLog>();

    /// <summary>
    /// Настройка структуры базы данных.
    /// 
    /// Здесь задаются индексы, ограничения уникальности,
    /// максимальная длина строк и связи между таблицами.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Обязательно вызываем базовую настройку Identity.
        // Без этого таблицы пользователей и ролей будут созданы неправильно.
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(x => x.Client)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ApplicationUser>()
            .Property(x => x.Country)
            .HasMaxLength(100);

        builder.Entity<ApplicationUser>()
            .Property(x => x.AutoTranslateMessages)
            .HasDefaultValue(true);

        builder.Entity<ApplicationUser>()
            .Property(x => x.WorkdayStartMinutes)
            .HasDefaultValue(8 * 60);

        builder.Entity<ApplicationUser>()
            .Property(x => x.WorkdayEndMinutes)
            .HasDefaultValue(17 * 60);

        // Лицензионный ключ должен быть уникальным.
        // Один ключ не может принадлежать двум станкам.
        builder.Entity<Machine>()
            .HasIndex(x => x.LicenseKey)
            .IsUnique();

        // Серийный номер станка тоже должен быть уникальным.
        builder.Entity<Machine>()
            .HasIndex(x => x.SerialNumber)
            .IsUnique();

        // Ограничиваем длину названия станка.
        builder.Entity<Machine>()
            .Property(x => x.Name)
            .HasMaxLength(200);

        // Ограничиваем длину модели станка.
        builder.Entity<Machine>()
            .Property(x => x.Model)
            .HasMaxLength(100);

        // Ограничиваем длину серийного номера.
        builder.Entity<Machine>()
            .Property(x => x.SerialNumber)
            .HasMaxLength(100);

        // Ограничиваем длину лицензионного ключа.
        builder.Entity<Machine>()
            .Property(x => x.LicenseKey)
            .HasMaxLength(100);

        builder.Entity<Client>()
            .Property(x => x.Name)
            .HasMaxLength(200);

        builder.Entity<License>()
            .HasIndex(x => x.Key)
            .IsUnique();

        builder.Entity<License>()
            .HasOne(x => x.ActivatedByUser)
            .WithMany()
            .HasForeignKey(x => x.ActivatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<License>()
            .Property(x => x.Key)
            .HasMaxLength(100);

        builder.Entity<ClientMachine>()
            .HasIndex(x => new { x.ClientId, x.MachineId })
            .IsUnique();

        builder.Entity<Ticket>()
            .HasIndex(x => x.Status);

        builder.Entity<Ticket>()
            .Property(x => x.Title)
            .HasMaxLength(300);

        builder.Entity<Ticket>()
            .Property(x => x.Status)
            .HasMaxLength(50);

        builder.Entity<EmailNotificationLog>()
            .HasIndex(x => new { x.NotificationType, x.TicketId, x.ChatMessageId, x.RecipientUserId })
            .IsUnique();

        builder.Entity<EmailNotificationLog>()
            .Property(x => x.NotificationType)
            .HasMaxLength(80);

        builder.Entity<EmailNotificationLog>()
            .Property(x => x.RecipientEmail)
            .HasMaxLength(256);

        builder.Entity<EmailNotificationLog>()
            .HasOne(x => x.Ticket)
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EmailNotificationLog>()
            .HasOne(x => x.ChatMessage)
            .WithMany()
            .HasForeignKey(x => x.ChatMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EmailNotificationLog>()
            .HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<TicketOperatorAssignment>()
            .HasIndex(x => new { x.TicketId, x.OperatorUserId })
            .IsUnique();

        builder.Entity<TicketOperatorAssignment>()
            .HasOne(x => x.Ticket)
            .WithMany(x => x.OperatorAssignments)
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TicketOperatorAssignment>()
            .HasOne(x => x.OperatorUser)
            .WithMany()
            .HasForeignKey(x => x.OperatorUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Attachment>()
            .Property(x => x.OriginalFileName)
            .HasMaxLength(255);

        builder.Entity<Attachment>()
            .Property(x => x.StoredFileName)
            .HasMaxLength(255);

        builder.Entity<Attachment>()
            .Property(x => x.Status)
            .HasMaxLength(40);

        builder.Entity<ChatMessageTranslation>()
            .HasIndex(x => new { x.ChatMessageId, x.TargetLanguage })
            .IsUnique();

        builder.Entity<ChatMessageTranslation>()
            .HasOne(x => x.ChatMessage)
            .WithMany(x => x.Translations)
            .HasForeignKey(x => x.ChatMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChatMessageTranslation>()
            .Property(x => x.TargetLanguage)
            .HasMaxLength(80);

        builder.Entity<ChatMessageTranslation>()
            .Property(x => x.SourceText)
            .HasDefaultValue(string.Empty);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Source)
            .HasMaxLength(80);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Tags)
            .HasMaxLength(500);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.FileName)
            .HasMaxLength(255);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.SectionTitle)
            .HasMaxLength(300);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.ErrorName)
            .HasMaxLength(300);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.ErrorCode)
            .HasMaxLength(80);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.NodeName)
            .HasMaxLength(200);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.DocumentType)
            .HasMaxLength(80);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Title)
            .HasMaxLength(300);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Cause)
            .HasMaxLength(1000);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Solution)
            .HasMaxLength(1500);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.SubsectionTitle)
            .HasMaxLength(300);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.SheetName)
            .HasMaxLength(200);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.ColumnNames)
            .HasMaxLength(1000);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.ChatDate)
            .HasMaxLength(100);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Participants)
            .HasMaxLength(500);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Topic)
            .HasMaxLength(300);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.SourceChat)
            .HasMaxLength(300);

        // Ограничиваем длину категории документа.
        builder.Entity<KnowledgeDocument>()
            .Property(x => x.Category)
            .HasMaxLength(100);

        builder.Entity<KnowledgeDocument>()
            .Property(x => x.SerialNumber)
            .HasMaxLength(100);

        // Ограничиваем длину категории чанка.
        builder.Entity<KnowledgeChunk>()
            .Property(x => x.Category)
            .HasMaxLength(100);

        builder.Entity<KnowledgeChunk>()
            .Property(x => x.SerialNumber)
            .HasMaxLength(100);

        builder.Entity<KnowledgeChunk>()
            .HasOne(x => x.ResolvedAnswer)
            .WithMany()
            .HasForeignKey(x => x.ResolvedAnswerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<KnowledgeChunk>()
            .HasOne(x => x.QAEntry)
            .WithMany(x => x.Chunks)
            .HasForeignKey(x => x.QAEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ResolvedAnswer>()
            .Property(x => x.Category)
            .HasMaxLength(100);

        builder.Entity<KnowledgeCategory>()
            .HasIndex(x => x.Name)
            .IsUnique();

        builder.Entity<KnowledgeCategory>()
            .Property(x => x.Name)
            .HasMaxLength(100);

        builder.Entity<SystemSetting>()
            .HasIndex(x => x.Key)
            .IsUnique();

        builder.Entity<SystemSetting>()
            .Property(x => x.Key)
            .HasMaxLength(120);

        builder.Entity<QAEntry>()
            .Property(x => x.Question)
            .HasMaxLength(1000);

        builder.Entity<QAEntry>()
            .Property(x => x.Status)
            .HasMaxLength(40);

        builder.Entity<QAEntry>()
            .Property(x => x.Source)
            .HasMaxLength(40);

        builder.Entity<QAEntry>()
            .Property(x => x.MachineModel)
            .HasMaxLength(100);

        builder.Entity<QAEntry>()
            .Property(x => x.SerialNumber)
            .HasMaxLength(100);

        builder.Entity<QAEntry>()
            .Property(x => x.NodeName)
            .HasMaxLength(200);

        builder.Entity<QAEntry>()
            .Property(x => x.Category)
            .HasMaxLength(100);

        builder.Entity<QAEntry>()
            .Property(x => x.ProblemType)
            .HasMaxLength(100);

        builder.Entity<QAEntry>()
            .HasIndex(x => x.Status);

        builder.Entity<QAAttachment>()
            .HasOne(x => x.QAEntry)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.QAEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QAAttachment>()
            .Property(x => x.OriginalFileName)
            .HasMaxLength(255);

        builder.Entity<QAAttachment>()
            .Property(x => x.StoredFileName)
            .HasMaxLength(255);

        builder.Entity<QAEntry>()
            .HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<OperatorChatPresence>()
            .HasIndex(x => x.OperatorUserId)
            .IsUnique();

        builder.Entity<OperatorChatPresence>()
            .HasOne(x => x.OperatorUser)
            .WithMany()
            .HasForeignKey(x => x.OperatorUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OperatorChatPresence>()
            .HasOne(x => x.Ticket)
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OperatorChatTimeEntry>()
            .HasIndex(x => new { x.OperatorUserId, x.StartedAt });

        builder.Entity<OperatorChatTimeEntry>()
            .HasIndex(x => new { x.MachineId, x.StartedAt });

        builder.Entity<OperatorChatTimeEntry>()
            .HasOne(x => x.OperatorUser)
            .WithMany()
            .HasForeignKey(x => x.OperatorUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OperatorChatTimeEntry>()
            .HasOne(x => x.Ticket)
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OperatorChatTimeEntry>()
            .HasOne(x => x.Machine)
            .WithMany()
            .HasForeignKey(x => x.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
