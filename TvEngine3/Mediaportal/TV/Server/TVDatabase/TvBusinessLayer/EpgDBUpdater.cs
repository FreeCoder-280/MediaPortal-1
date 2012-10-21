#region Copyright (C) 2005-2011 Team MediaPortal

// Copyright (C) 2005-2011 Team MediaPortal
// http://www.team-mediaportal.com
// 
// MediaPortal is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// MediaPortal is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MediaPortal. If not, see <http://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlTypes;
using Mediaportal.TV.Server.TVDatabase.Entities;
using Mediaportal.TV.Server.TVDatabase.Entities.Enums;
using Mediaportal.TV.Server.TVDatabase.Entities.Factories;
using Mediaportal.TV.Server.TVDatabase.TVBusinessLayer.Entities;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Epg;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Implementations.Channels;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Interfaces;
using MediaPortal.Common.Utils;

namespace Mediaportal.TV.Server.TVDatabase.TVBusinessLayer
{
  public class EpgHole
  {
    public DateTime start;
    public DateTime end;

    public EpgHole(DateTime start, DateTime end)
    {
      this.start = start;
      this.end = end;
    }

    public bool FitsInHole(DateTime startParam, DateTime endParam)
    {
      return (startParam >= start && endParam <= end);
    }
  }

  public class EpgHoleCollection : List<EpgHole>
  {
    public bool FitsInAnyHole(DateTime start, DateTime end)
    {
      foreach (EpgHole hole in this)
      {
        if (hole.FitsInHole(start, end))
        {
          return true;
        }
      }
      return false;
    }
  }

  public class EpgDBUpdater : LogProvider
  {
      // TODO log4net gibman : we want fileappender specifically for EPG / Mediaportal.TV.Server.TVDatabase.TVBusinessLayer.EpgDBUpdater
    #region logging

    private static ILogManager Log
    {
        get { return LogHelper.GetLogger(typeof(EpgDBUpdater)); }
    }

    #endregion
    #region Variables

    private readonly IEpgEvents _epgEvents;
    private string _titleTemplate;
    private string _descriptionTemplate;
    private string _epgLanguages;
    private readonly string _grabberName;
    private bool _storeOnlySelectedChannels;
    private bool _storeOnlySelectedChannelsRadio;
    private readonly bool _checkForLastUpdate;
    private int _epgReGrabAfter = 240; //4 hours
    private bool _alwaysFillHoles;
    private bool _alwaysReplace;

    #endregion

    #region ctor

    public EpgDBUpdater(IEpgEvents epgEvents, string grabberName, bool checkForLastUpdate)
    {
      _epgEvents = epgEvents;
      _grabberName = grabberName;
      _checkForLastUpdate = checkForLastUpdate;
      ReloadConfig();
      if (_alwaysReplace)
      {
        _checkForLastUpdate = false;
      }
    }

    #endregion

    #region Public members

    public void ReloadConfig()
    {      
      
      _titleTemplate = SettingsManagement.GetSetting("epgTitleTemplate", "%TITLE%").Value;
      _descriptionTemplate = SettingsManagement.GetSetting("epgDescriptionTemplate", "%DESCRIPTION%").Value;
      _epgLanguages = SettingsManagement.GetSetting("epgLanguages").Value;
      Setting setting = SettingsManagement.GetSetting("epgStoreOnlySelected");
      _storeOnlySelectedChannels = (setting.Value == "yes");
      Setting settingRadio = SettingsManagement.GetSetting("epgRadioStoreOnlySelected");
      _storeOnlySelectedChannelsRadio = (settingRadio.Value == "yes");
      Setting s = SettingsManagement.GetSetting("timeoutEPGRefresh", "240");
      if (Int32.TryParse(s.Value, out _epgReGrabAfter) == false)
      {
        _epgReGrabAfter = 240;
      }
      _alwaysFillHoles = (SettingsManagement.GetSetting("generalEPGAlwaysFillHoles", "no").Value == "yes");
      _alwaysReplace = (SettingsManagement.GetSetting("generalEPGAlwaysReplace", "no").Value == "yes");
    }

    public void UpdateEpgForChannel(EpgChannel epgChannel)
    {
      Channel dbChannel = IsInsertAllowed(epgChannel);
      if (dbChannel == null)
      {
        return;
      }
      Log.DebugFormat("{0}: {1} lastUpdate:{2}", _grabberName, dbChannel.DisplayName, dbChannel.LastGrabTime);

      // Store the data in our database
      ImportPrograms(dbChannel, epgChannel.Programs);
      // Raise an event with the data so that other plugins can handle the data on their own
      _epgEvents.OnImportEpgPrograms(epgChannel);
    }

    private void ImportPrograms(Channel dbChannel, IList<EpgProgram> epgPrograms)
    {
      int iInserted = 0;
      bool hasGaps = false;

      ProgramManagement.DeleteOldPrograms(dbChannel.IdChannel);

      EpgHoleCollection holes = new EpgHoleCollection();
      if ((dbChannel.EpgHasGaps || _alwaysFillHoles) && !_alwaysReplace)
      {
          Log.DebugFormat("{0}: {1} is marked to have epg gaps. Calculating them...", _grabberName, dbChannel.DisplayName);

        IList<Program> infos = ProgramManagement.GetPrograms(dbChannel.IdChannel, DateTime.Now);
        if (infos.Count > 1)
        {
          for (int i = 1; i < infos.Count; i++)
          {
            Program prev = infos[i - 1];
            Program current = infos[i];
            TimeSpan diff = current.StartTime - prev.EndTime;
            if (diff.TotalMinutes > 5)
            {
              holes.Add(new EpgHole(prev.EndTime, current.StartTime));
            }
          }
        }
        Log.DebugFormat("{0}: {1} Found {2} epg holes.", _grabberName, dbChannel.DisplayName, holes.Count);
      }
      DateTime dbLastProgram = ProgramManagement.GetNewestProgramForChannel(dbChannel.IdChannel);
      EpgProgram lastProgram = null;
      for (int i = 0; i < epgPrograms.Count; i++)
      {
        EpgProgram epgProgram = epgPrograms[i];
        // Check for dupes
        if (lastProgram != null)
        {
          if (epgProgram.StartTime == lastProgram.StartTime && epgProgram.EndTime == lastProgram.EndTime)
          {
            continue;
          }
          TimeSpan diff = epgProgram.StartTime - lastProgram.EndTime;
          if (diff.Minutes > 5)
          {
            hasGaps = true;
          }
        }
        if (epgProgram.StartTime <= dbLastProgram && !_alwaysReplace)
        {
          if (epgProgram.StartTime < DateTime.Now)
          {
            continue;
          }
          if (!holes.FitsInAnyHole(epgProgram.StartTime, epgProgram.StartTime))
          {
            continue;
          }
          Log.DebugFormat("{0}: Great we stuffed an epg hole {1}-{2} :-)", _grabberName,
                  epgProgram.StartTime.ToShortDateString() + " " + epgProgram.StartTime.ToShortTimeString(),
                  epgProgram.EndTime.ToShortDateString() + " " + epgProgram.EndTime.ToShortTimeString());
        }
        Program prog = null;
        if (_alwaysReplace)
        {
          try
          {
            IList<Program> epgs = ProgramManagement.GetProgramExists(dbChannel.IdChannel, epgProgram.StartTime, epgProgram.EndTime);

            if (epgs.Count > 0)
            {
              prog = epgs[0];
              if (epgs.Count > 1)
              {
                Log.DebugFormat("- {0} entries are obsolete for {1} from {2} to {3}", epgs.Count - 1, dbChannel.DisplayName,
                        epgProgram.StartTime, epgProgram.EndTime);
              }
              for (int idx = 1; idx < epgs.Count; idx++)
              {
                try
                {                  
                  ProgramManagement.DeleteProgram(epgs[idx].IdProgram);
                  Log.DebugFormat("- Deleted the epg entry {0} ({1} - {2})", epgs[idx].Title, epgs[idx].StartTime,
                          epgs[idx].EndTime);
                }
                catch (Exception ex)
                {
                  Log.ErrorFormat(ex, "Error during epg entry deletion");
                }
              }
            }
          }
          catch (Exception ex)
          {
            Log.ErrorFormat(ex, "Error the existing epg entry check");
          }
        }
        AddProgramAndApplyTemplates(dbChannel, epgProgram, prog);
        iInserted++;
        lastProgram = epgProgram;
      }
      dbChannel.LastGrabTime = DateTime.Now;
      dbChannel.EpgHasGaps = hasGaps;
      ChannelManagement.SaveChannel(dbChannel);      

      //_layer.StartResetProgramStatesThread(System.Threading.ThreadPriority.Lowest);


      Log.DebugFormat("- Inserted {0} epg entries for channel {1}", iInserted, dbChannel.DisplayName);
    }

    #endregion

    #region Private functions

    private Channel IsInsertAllowed(EpgChannel epgChannel)
    {
      DVBBaseChannel dvbChannel = (DVBBaseChannel)epgChannel.Channel;
      //are there any epg infos for this channel?
      if (epgChannel.Programs.Count == 0)
      {
        Log.InfoFormat("{0}: no epg infos found for channel networkid:0x{1:X} transportid:0x{2:X} serviceid:0x{3:X}",
                _grabberName, dvbChannel.NetworkId, dvbChannel.TransportId, dvbChannel.ServiceId);
        return null;
      }
      //do we know a channel with these tuning details?
      Channel dbChannel = null;
      TuningDetail tuningDetail = ChannelManagement.GetTuningDetail(dvbChannel);      
      if (tuningDetail != null)
      {
        dbChannel = tuningDetail.Channel;
      }

      if (dbChannel == null)
      {
        Log.InfoFormat("{0}: no channel found for networkid:0x{1:X} transportid:0x{2:X} serviceid:0x{3:X}", _grabberName,
                dvbChannel.NetworkId, dvbChannel.TransportId, dvbChannel.ServiceId);
        /*foreach (EpgProgram ei in epgChannel.Programs)
        {
          string title = "";
          if (ei.Text.Count > 0)
          {
            title = ei.Text[0].Title;
          }
          Log.Epg("                   -> {0}-{1}  {2}", ei.startTime, ei.endTime, title);
        }*/
        return null;
      }
      //should we store epg for this channel?
      var isRadio = dbChannel.MediaType == (decimal) MediaTypeEnum.Radio;
      if ((isRadio && _storeOnlySelectedChannelsRadio) || (!isRadio && _storeOnlySelectedChannels))
      {
        if (!dbChannel.GrabEpg)
        {
          Log.InfoFormat("{0}: channel {1} is not configured to grab epg.", _grabberName, dbChannel.DisplayName);
          return null;
        }
      }
      if (_checkForLastUpdate)
      {
        //is the regrab time reached?
        TimeSpan ts = DateTime.Now - dbChannel.LastGrabTime.GetValueOrDefault(DateTime.MinValue);
        if (ts.TotalMinutes < _epgReGrabAfter)
        {
          Log.InfoFormat("{0}: {1} not needed lastUpdate:{2}", _grabberName, dbChannel.DisplayName, dbChannel.LastGrabTime);
          return null;
        }
      }
      return dbChannel;
    }

    #region Template Tools

    private static string GetStarRatingStr(int starRating)
    {
      string rating = "";
      switch (starRating)
      {
        case 1:
          rating = "*";
          break;
        case 2:
          rating = "*+";
          break;
        case 3:
          rating = "**";
          break;
        case 4:
          rating = "**+";
          break;
        case 5:
          rating = "***";
          break;
        case 6:
          rating = "***+";
          break;
        case 7:
          rating = "****";
          break;
      }
      return rating;
    }

    private static string EvalTemplate(string template, NameValueCollection values)
    {
      for (int i = 0; i < values.Count; i++)
      {
        template = template.Replace(values.Keys[i], values[i]);
      }
      return template;
    }

    #endregion

    #region Single Program Updating Tools

    private void GetEPGLanguage(IList<EpgLanguageText> texts, out string title, out string description, out string genre,
                                out int starRating, out string classification, out int parentalRating)
    {
      title = "";
      description = "";
      genre = "";
      starRating = 0;
      classification = "";

      parentalRating = -1;

      if (texts.Count != 0)
      {
        int offset = -1;
        for (int i = 0; i < texts.Count; ++i)
        {
          if (texts[0].Language.ToLowerInvariant() == "all")
          {
            offset = i;
            break;
          }
          if (_epgLanguages.Length == 0 ||
              _epgLanguages.ToLowerInvariant().IndexOf(texts[i].Language.ToLowerInvariant()) >= 0)
          {
            offset = i;
            break;
          }
        }
        if (offset != -1)
        {
          title = texts[offset].Title;
          description = texts[offset].Description;
          genre = texts[offset].Genre;
          starRating = texts[offset].StarRating;
          classification = texts[offset].Classification;
          parentalRating = texts[offset].ParentalRating;
        }
        else
        {
          title = texts[0].Title;
          description = texts[0].Description;
          genre = texts[0].Genre;
          starRating = texts[0].StarRating;
          classification = texts[0].Classification;
          parentalRating = texts[0].ParentalRating;
        }
      }

      if (title == null)
      {
        title = "";
      }
      if (description == null)
      {
        description = "";
      }
      if (genre == null)
      {
        genre = "";
      }
      if (classification == null)
      {
        classification = "";
      }
    }

    private void AddProgramAndApplyTemplates(Channel dbChannel, EpgProgram ep, Program dbProg)
    {
      string title;
      string description;
      string genre;
      int starRating;
      string classification;
      int parentRating;
      GetEPGLanguage(ep.Text, out title, out description, out genre, out starRating, out classification,
                     out parentRating);
      NameValueCollection values = new NameValueCollection();
      values.Add("%TITLE%", title);
      values.Add("%DESCRIPTION%", description);
      values.Add("%GENRE%", genre);
      values.Add("%STARRATING%", starRating.ToString());
      values.Add("%STARRATING_STR%", GetStarRatingStr(starRating));
      values.Add("%CLASSIFICATION%", classification);
      values.Add("%PARENTALRATING%", parentRating.ToString());
      values.Add("%NEWLINE%", Environment.NewLine);
      title = EvalTemplate(_titleTemplate, values);
      description = EvalTemplate(_descriptionTemplate, values);
      if (dbProg == null)
      {
        dbProg = ProgramFactory.CreateProgram(dbChannel.IdChannel, ep.StartTime, ep.EndTime, title, description, ProgramManagement.GetProgramCategoryByName(genre),
                                     ProgramState.None,
                                     SqlDateTime.MinValue.Value, string.Empty, string.Empty, string.Empty, string.Empty,
                                     starRating, classification,
                                     parentRating);
        ProgramManagement.SaveProgram(dbProg);
      }
      else
      {
        ProgramBLL prgBLL = new ProgramBLL(dbProg);
        // this prevents a more detailed description getting overriden by a short description from another transponder
        if (prgBLL.Entity.Title == title)
        {
          if (prgBLL.Entity.Description.Length < description.Length)
          {
            prgBLL.Entity.Description = description;
          }
        }
        else
        {
          prgBLL.Entity.Description = description;
        }
        prgBLL.Entity.Title = title;
        prgBLL.Entity.StartTime = ep.StartTime;
        prgBLL.Entity.EndTime = ep.EndTime;
        prgBLL.Entity.ProgramCategory = ProgramManagement.GetProgramCategoryByName(genre);
        prgBLL.Entity.StarRating = starRating;
        prgBLL.Entity.Classification = classification;
        prgBLL.Entity.ParentalRating = parentRating;
        prgBLL.Entity.OriginalAirDate = SqlDateTime.MinValue.Value; // TODO: /!\ add implementation
        prgBLL.ClearRecordPendingState();
        ProgramManagement.SaveProgram(prgBLL.Entity);
      }      
    }

    #endregion

    #endregion
  }
}