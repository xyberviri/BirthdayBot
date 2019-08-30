﻿Imports System.Data.Common
Imports Discord.WebSocket
Imports Npgsql
Imports NpgsqlTypes

''' <summary>
''' Holds various pieces of state information for a guild the bot is operating in.
''' Includes, among other things, a copy of the guild's settings and a list of all known users with birthdays.
''' </summary>
Friend Class GuildStateInformation
    Public ReadOnly Property GuildId As ULong
    Private ReadOnly _db As Database
    Private _bdayRole As ULong?
    Private _announceCh As ULong?
    Private _modRole As ULong?
    Private _tz As String
    Private _moderated As Boolean
    Private _announceMsg As String
    Private _announceMsgPl As String
    Private _announcePing As Boolean
    Private ReadOnly _userCache As Dictionary(Of ULong, GuildUserSettings)

    Private _roleLastWarning As New DateTimeOffset(DateTime.MinValue, TimeSpan.Zero)
    Private Shared ReadOnly RoleWarningInterval As New TimeSpan(1, 0, 0)

    ''' <summary>
    ''' Message for notifying servers that the bot is unable to manipulate its designated role for various reasons.
    ''' The returned value is dependent on certain warning flags accessible in this class. To avoid bombarding users
    ''' with the same message, this property only returns a non-Nothing value at most once per hour.
    ''' Otherwise, it shall always return Nothing if there is no warning to be issued.
    ''' </summary>
    Public ReadOnly Property IssueRoleWarning As String
        Get
            If DateTimeOffset.UtcNow - _roleLastWarning > RoleWarningInterval Then
                _roleLastWarning = DateTimeOffset.UtcNow
            Else
                Return Nothing
            End If

            If RoleWarningUnset Or RoleWarningNonexist Then
                Return "Warning: A birthday role must be configured before this bot can function properly. " +
                    "Update the designated role with `bb.config role (role name/ID)`."
            End If
            If RoleWarningPermission Then
                Return "Warning: This bot is unable to set the birthday role onto users. " +
                    "Make sure that this bot has the Manage Roles permission and is not placed below the birthday role."
            End If

            Return Nothing
        End Get
    End Property

    ''' <summary>
    ''' Role warning message: The birthday role cannot be accessed.
    ''' </summary>
    Friend Property RoleWarningPermission As Boolean = False

    ''' <summary>
    ''' Role warning message: The birthday role no longer exists.
    ''' </summary>
    Friend Property RoleWarningNonexist As Boolean = False

    ''' <summary>
    ''' Role warning message: The birthday role is not set.
    ''' </summary>
    Friend ReadOnly Property RoleWarningUnset As Boolean
        Get
            Return _bdayRole Is Nothing
        End Get
    End Property

    ''' <summary>
    ''' Gets a list of cached users. Use sparingly.
    ''' </summary>
    Public ReadOnly Property Users As IEnumerable(Of GuildUserSettings)
        Get
            Dim items As New List(Of GuildUserSettings)
            For Each item In _userCache.Values
                items.Add(item)
            Next
            Return items
        End Get
    End Property

    ''' <summary>
    ''' Gets the guild's designated Role ID.
    ''' </summary>
    Public ReadOnly Property RoleId As ULong?
        Get
            Return _bdayRole
        End Get
    End Property

    ''' <summary>
    ''' Gets the designated announcement Channel ID.
    ''' </summary>
    Public ReadOnly Property AnnounceChannelId As ULong?
        Get
            Return _announceCh
        End Get
    End Property

    ''' <summary>
    ''' Gets the guild's default time zone.
    ''' </summary>
    Public ReadOnly Property TimeZone As String
        Get
            Return _tz
        End Get
    End Property

    ''' <summary>
    ''' Gets whether the guild is in moderated mode.
    ''' </summary>
    Public ReadOnly Property IsModerated As Boolean
        Get
            Return _moderated
        End Get
    End Property

    ''' <summary>
    ''' Gets the designated moderator role ID.
    ''' </summary>
    Public ReadOnly Property ModeratorRole As ULong?
        Get
            Return _modRole
        End Get
    End Property

    ''' <summary>
    ''' Gets the guild-specific birthday announcement message.
    ''' </summary>
    Public ReadOnly Property AnnounceMessages As (String, String)
        Get
            Return (_announceMsg, _announceMsgPl)
        End Get
    End Property

    ''' <summary>
    ''' Gets whether to ping users in the announcement message instead of displaying their names.
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property AnnouncePing As Boolean
        Get
            Return _announcePing
        End Get
    End Property

    ' Called by LoadSettingsAsync. Double-check ordinals when changes are made.
    Private Sub New(reader As DbDataReader, dbconfig As Database)
        _db = dbconfig
        GuildId = CULng(reader.GetInt64(0))
        ' Weird: if using a ternary operator with a ULong?, Nothing resolves to 0 despite Option Strict On.
        If Not reader.IsDBNull(1) Then
            _bdayRole = CULng(reader.GetInt64(1))
        End If
        If Not reader.IsDBNull(2) Then _announceCh = CULng(reader.GetInt64(2))
        _tz = If(reader.IsDBNull(3), Nothing, reader.GetString(3))
        _moderated = reader.GetBoolean(4)
        If Not reader.IsDBNull(5) Then _modRole = CULng(reader.GetInt64(5))
        _announceMsg = If(reader.IsDBNull(6), Nothing, reader.GetString(6))
        _announceMsgPl = If(reader.IsDBNull(7), Nothing, reader.GetString(7))
        _announcePing = reader.GetBoolean(8)

        ' Get user information loaded up.
        Dim userresult = GuildUserSettings.GetGuildUsersAsync(dbconfig, GuildId)
        _userCache = New Dictionary(Of ULong, GuildUserSettings)
        For Each item In userresult
            _userCache.Add(item.UserId, item)
        Next
    End Sub

    ''' <summary>
    ''' Gets user information from this guild. If the user doesn't exist in the backing database,
    ''' a new instance is created which is capable of adding the user to the database.
    ''' </summary>
    ''' <param name="userId"></param>
    Public Function GetUser(userId As ULong) As GuildUserSettings
        If _userCache.ContainsKey(userId) Then
            Return _userCache(userId)
        End If

        ' No result. Create a blank entry and add it to the list, in case it
        ' gets referenced later regardless of if having been updated or not.
        Dim blank As New GuildUserSettings(_GuildId, userId)
        _userCache.Add(userId, blank)
        Return blank
    End Function

    ''' <summary>
    ''' Deletes the user from the backing database. Drops the locally cached entry.
    ''' </summary>
    Public Async Function DeleteUserAsync(userId As ULong) As Task
        Dim user As GuildUserSettings = Nothing
        If _userCache.TryGetValue(userId, user) Then
            Await user.DeleteAsync(_db)
        Else
            Return
        End If
        _userCache.Remove(userId)
    End Function

    ''' <summary>
    ''' Checks if the given user is blocked from issuing commands.
    ''' If the server is in moderated mode, this always returns True.
    ''' Does not check if the user is a manager.
    ''' </summary>
    Public Async Function IsUserBlockedAsync(userId As ULong) As Task(Of Boolean)
        If IsModerated Then Return True

        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"select * from {BackingTableBans} " +
                    "where guild_id = @Gid and user_id = @Uid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(GuildId)
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = CLng(userId)
                c.Prepare()
                Using r = Await c.ExecuteReaderAsync()
                    If Await r.ReadAsync() Then Return True
                    Return False
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Checks if the given user is a moderator either by having the Manage Server permission or
    ''' being in the designated modeartor role.
    ''' </summary>
    Public Function IsUserModerator(user As SocketGuildUser) As Boolean
        If user.GuildPermissions.ManageGuild Then Return True
        If ModeratorRole.HasValue Then
            If user.Roles.Where(Function(r) r.Id = ModeratorRole.Value).Count > 0 Then Return True
        End If

        IsUserModerator = False
    End Function

    ''' <summary>
    ''' Adds the specified user to the block list, preventing them from issuing commands.
    ''' </summary>
    Public Async Function BlockUserAsync(userId As ULong) As Task
        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"insert into {BackingTableBans} (guild_id, user_id) " +
                    "values (@Gid, @Uid) " +
                    "on conflict (guild_id, user_id) do nothing"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(GuildId)
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = CLng(userId)
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Removes the specified user from the block list.
    ''' </summary>
    Public Async Function UnbanUserAsync(userId As ULong) As Task
        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"delete from {BackingTableBans} where " +
                    "guild_id = @Gid and user_id = @Uid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(GuildId)
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = CLng(userId)
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function

    Public Async Function UpdateRoleAsync(roleId As ULong) As Task
        _bdayRole = roleId
        _roleLastWarning = New DateTimeOffset
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateAnnounceChannelAsync(channelId As ULong?) As Task
        _announceCh = channelId
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateTimeZoneAsync(tzString As String) As Task
        _tz = tzString
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateModeratedModeAsync(isModerated As Boolean) As Task
        _moderated = isModerated
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateModeratorRoleAsync(roleId As ULong?) As Task
        _modRole = roleId
        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateAnnounceMessageAsync(message As String, plural As Boolean) As Task
        If plural Then
            _announceMsgPl = message
        Else
            _announceMsg = message
        End If

        Await UpdateDatabaseAsync()
    End Function

    Public Async Function UpdateAnnouncePingAsync(value As Boolean) As Task
        _announcePing = value
        Await UpdateDatabaseAsync()
    End Function

#Region "Database"
    Public Const BackingTable = "settings"
    Public Const BackingTableBans = "banned_users"

    Friend Shared Sub SetUpDatabaseTable(db As NpgsqlConnection)
        Using c = db.CreateCommand()
            c.CommandText = $"create table if not exists {BackingTable} (" +
                "guild_id bigint primary key, " +
                "role_id bigint null, " +
                "channel_announce_id bigint null, " +
                "time_zone text null, " +
                "moderated boolean not null default FALSE, " +
                "moderator_role bigint null, " +
                "announce_message text null, " +
                "announce_message_pl text null, " +
                "announce_ping boolean not null default FALSE" +
                ")"
            c.ExecuteNonQuery()
        End Using
        Using c = db.CreateCommand()
            c.CommandText = $"create table if not exists {BackingTableBans} (" +
                $"guild_id bigint not null references {BackingTable}, " +
                "user_id bigint not null, " +
                "PRIMARY KEY (guild_id, user_id)" +
                ")"
            c.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>
    ''' Retrieves an object instance representative of guild settings for the specified guild.
    ''' If settings for the given guild do not yet exist, a new value is created.
    ''' </summary>
    Friend Shared Async Function LoadSettingsAsync(dbsettings As Database, guild As ULong) As Task(Of GuildStateInformation)
        Using db = Await dbsettings.OpenConnectionAsync()
            Using c = db.CreateCommand()
                ' Take note of ordinals for use in the constructor
                c.CommandText = "select guild_id, role_id, channel_announce_id, time_zone, moderated, moderator_role, announce_message, announce_message_pl, announce_ping " +
                    $"from {BackingTable} where guild_id = @Gid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(guild)
                c.Prepare()
                Using r = Await c.ExecuteReaderAsync()
                    If Await r.ReadAsync() Then
                        Return New GuildStateInformation(r, dbsettings)
                    End If
                End Using
            End Using

            ' If we got here, no row exists. Create it.
            Using c = db.CreateCommand()
                c.CommandText = $"insert into {BackingTable} (guild_id) values (@Gid)"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(guild)
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
        ' New row created. Try this again.
        Return Await LoadSettingsAsync(dbsettings, guild)
    End Function

    ''' <summary>
    ''' Updates the backing database with values from this instance
    ''' This is a non-asynchronous operation. That may be bad.
    ''' </summary>
    Private Async Function UpdateDatabaseAsync() As Task
        Using db = Await _db.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = $"update {BackingTable} set " +
                    "role_id = @RoleId, " +
                    "channel_announce_id = @ChannelId, " +
                    "time_zone = @TimeZone, " +
                    "moderated = @Moderated, " +
                    "moderator_role = @ModRole, " +
                    "announce_message = @AnnounceMsg, " +
                    "announce_message_pl = @AnnounceMsgPl, " +
                    "announce_ping = @AnnouncePing " +
                    "where guild_id = @Gid"
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = CLng(GuildId)
                With c.Parameters.Add("@RoleId", NpgsqlDbType.Bigint)
                    If RoleId.HasValue Then
                        .Value = CLng(RoleId.Value)
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                With c.Parameters.Add("@ChannelId", NpgsqlDbType.Bigint)
                    If _announceCh.HasValue Then
                        .Value = CLng(_announceCh.Value)
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                With c.Parameters.Add("@TimeZone", NpgsqlDbType.Text)
                    If _tz IsNot Nothing Then
                        .Value = _tz
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                c.Parameters.Add("@Moderated", NpgsqlDbType.Boolean).Value = _moderated
                With c.Parameters.Add("@ModRole", NpgsqlDbType.Bigint)
                    If ModeratorRole.HasValue Then
                        .Value = CLng(ModeratorRole.Value)
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                With c.Parameters.Add("@AnnounceMsg", NpgsqlDbType.Text)
                    If _announceMsg IsNot Nothing Then
                        .Value = _announceMsg
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                With c.Parameters.Add("@AnnounceMsgPl", NpgsqlDbType.Text)
                    If _announceMsgPl IsNot Nothing Then
                        .Value = _announceMsgPl
                    Else
                        .Value = DBNull.Value
                    End If
                End With
                c.Parameters.Add("@AnnouncePing", NpgsqlDbType.Boolean).Value = _announcePing
                c.Prepare()
                Await c.ExecuteNonQueryAsync()
            End Using
        End Using
    End Function
#End Region
End Class
