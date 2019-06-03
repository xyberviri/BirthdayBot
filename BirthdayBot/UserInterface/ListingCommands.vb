﻿Imports System.IO
Imports System.Text
Imports Discord.WebSocket

''' <summary>
''' Commands for listing upcoming and all birthdays.
''' </summary>
Class ListingCommands
    Inherits CommandsCommon
    Public Overrides ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
        Get
            Return New List(Of (String, CommandHandler)) From {
                ("list", AddressOf CmdList),
                ("upcoming", AddressOf CmdUpcoming),
                ("recent", AddressOf CmdUpcoming)
            }
        End Get
    End Property

    Sub New(inst As BirthdayBot, db As Configuration)
        MyBase.New(inst, db)
    End Sub

    ' Creates a file with all birthdays.
    Private Async Function CmdList(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        ' For now, we're restricting this command to moderators only. This may turn into an option later.
        Dim reqMod As Boolean
        SyncLock Instance.KnownGuilds
            reqMod = Instance.KnownGuilds(reqChannel.Guild.Id).IsUserModerator(reqUser)
        End SyncLock

        If Not reqMod Then
            Await reqChannel.SendMessageAsync(":x: Only bot moderators may use this command.")
            Return
        End If

        Dim bdlist = Await BuildList(reqChannel.Guild, False)

        Dim filepath = Path.GetTempPath() + "birthdaybot-" + reqChannel.Guild.Id.ToString() + ".txt"
        Using f = File.CreateText(filepath)
            f.WriteLine("Birthdays in " + reqChannel.Guild.Name)
            f.WriteLine()
            For Each item In bdlist
                Dim user = reqChannel.Guild.GetUser(item.UserId)
                If user Is Nothing Then Continue For ' User disappeared in the instant between getting list and processing
                f.Write($"● {MonthNames(item.BirthMonth)}-{item.BirthDay.ToString("00")}: ")
                f.Write(item.UserId)
                f.Write(" " + user.Username + "#" + user.Discriminator)
                If user.Nickname IsNot Nothing Then
                    f.Write(" - Nickname: " + user.Nickname)
                End If
                f.WriteLine()
            Next
            Await f.FlushAsync()
        End Using

        Try
            Await reqChannel.SendFileAsync(filepath, $"Exported {bdlist.Count} birthdays to file.")
        Catch ex As Discord.Net.HttpException
            reqChannel.SendMessageAsync(":x: Unable to send list due to a permissions issue. Check the 'Attach Files' permission.").Wait()
        Catch ex As Exception
            Log("Listing", ex.ToString())
            reqChannel.SendMessageAsync(":x: An internal error occurred. It has been reported to the bot owner.").Wait()
        Finally
            File.Delete(filepath)
        End Try
    End Function

    ' "Recent and upcoming birthdays"
    ' The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    Private Async Function CmdUpcoming(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task
        Dim now = DateTimeOffset.UtcNow
        Dim search = DateIndex(now.Month, now.Day) - 4 ' begin search 4 days prior to current date UTC
        If search <= 0 Then search = 366 - Math.Abs(search)

        Dim query = Await BuildList(reqChannel.Guild, True)
        If query.Count = 0 Then
            Await reqChannel.SendMessageAsync("There are currently no recent or upcoming birthdays.")
            Return
        End If

        Dim output As New StringBuilder()
        output.AppendLine("Recent and upcoming birthdays:")
        For count = 1 To 11 ' cover 11 days total (3 prior, current day, 7 upcoming
            Dim results = From item In query
                          Where item.DateIndex = search
                          Select item

            ' push up search by 1 now, in case we back out early
            search += 1
            If search > 366 Then search = 1 ' wrap to beginning of year

            If results.Count = 0 Then Continue For ' back out early

            ' Build sorted name list
            Dim names As New List(Of String)
            For Each item In results
                names.Add(item.DisplayName)
            Next
            names.Sort(StringComparer.InvariantCultureIgnoreCase)

            Dim first = True
            output.AppendLine()
            output.Append($"● `{MonthNames(results(0).BirthMonth)}-{results(0).BirthDay.ToString("00")}`: ")
            For Each item In names
                If first Then
                    first = False
                Else
                    output.Append(", ")
                End If
                output.Append(item)
            Next
        Next

        Await reqChannel.SendMessageAsync(output.ToString())
    End Function

    ''' <summary>
    ''' Fetches all guild birthdays and places them into an easily usable structure.
    ''' Users currently not in the guild are not included in the result.
    ''' </summary>
    Private Async Function BuildList(guild As SocketGuild, escapeFormat As Boolean) As Task(Of List(Of ListItem))
        Dim ping As Boolean
        SyncLock Instance.KnownGuilds
            ping = Instance.KnownGuilds(guild.Id).AnnouncePing
        End SyncLock

        Using db = Await BotConfig.DatabaseSettings.OpenConnectionAsync()
            Using c = db.CreateCommand()
                c.CommandText = "select user_id, birth_month, birth_day from " + GuildUserSettings.BackingTable +
                    " where guild_id = @Gid order by birth_month, birth_day"
                c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint).Value = guild.Id
                c.Prepare()
                Using r = Await c.ExecuteReaderAsync()
                    Dim result As New List(Of ListItem)
                    While Await r.ReadAsync()
                        Dim id = CULng(r.GetInt64(0))
                        Dim month = r.GetInt32(1)
                        Dim day = r.GetInt32(2)

                        Dim guildUser = guild.GetUser(id)
                        If guildUser Is Nothing Then Continue While ' Skip users not in guild

                        result.Add(New ListItem With {
                            .BirthMonth = month,
                            .BirthDay = day,
                            .DateIndex = DateIndex(month, day),
                            .UserId = guildUser.Id,
                            .DisplayName = FormatName(guildUser, False)
                        })
                    End While
                    Return result
                End Using
            End Using
        End Using
    End Function

    Private Function DateIndex(month As Integer, day As Integer) As Integer
        DateIndex = 0
        ' Add month offset
        If month > 1 Then DateIndex += 31 ' Offset January
        If month > 2 Then DateIndex += 29 ' Offset February (incl. leap day)
        If month > 3 Then DateIndex += 31 ' etc
        If month > 4 Then DateIndex += 30
        If month > 5 Then DateIndex += 31
        If month > 6 Then DateIndex += 30
        If month > 7 Then DateIndex += 31
        If month > 8 Then DateIndex += 31
        If month > 9 Then DateIndex += 30
        If month > 10 Then DateIndex += 31
        If month > 11 Then DateIndex += 30
        DateIndex += day
    End Function

    ''' <summary>
    ''' Translate from date index value to readable date.
    ''' </summary>
    Private Function FromDateIndex(ByVal index As Integer) As String
        ' oh no...
        FromDateIndex = "Jan"
        If index > 31 Then
            index -= 31
            FromDateIndex = "Feb"
        End If
        If index > 29 Then
            index -= 29
            FromDateIndex = "Mar"
        End If
        If index > 31 Then
            index -= 31
            FromDateIndex = "Apr"
        End If
        If index > 30 Then
            index -= 30
            FromDateIndex = "May"
        End If
        If index > 31 Then
            index -= 31
            FromDateIndex = "Jun"
        End If
        If index > 30 Then
            index -= 30
            FromDateIndex = "Jul"
        End If
        If index > 31 Then
            index -= 31
            FromDateIndex = "Aug"
        End If
        If index > 31 Then
            index -= 31
            FromDateIndex = "Sep"
        End If
        If index > 30 Then
            index -= 30
            FromDateIndex = "Oct"
        End If
        If index > 31 Then
            index -= 31
            FromDateIndex = "Nov"
        End If
        If index > 30 Then
            index -= 30
            FromDateIndex = "Dec"
        End If
        FromDateIndex += " " + index.ToString("00")
    End Function

    Private Structure ListItem
        Public Property DateIndex As Integer
        Public Property BirthMonth As Integer
        Public Property BirthDay As Integer
        Public Property UserId As ULong
        Public Property DisplayName As String
    End Structure
End Class