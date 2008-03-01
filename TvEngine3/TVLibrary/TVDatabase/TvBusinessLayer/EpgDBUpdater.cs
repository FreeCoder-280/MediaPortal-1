/* 
 *	Copyright (C) 2005-2008 Team MediaPortal
 *	http://www.team-mediaportal.com
 *
 *  This Program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2, or (at your option)
 *  any later version.
 *   
 *  This Program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 *  GNU General Public License for more details.
 *   
 *  You should have received a copy of the GNU General Public License
 *  along with GNU Make; see the file COPYING.  If not, write to
 *  the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA. 
 *  http://www.gnu.org/copyleft/gpl.html
 *
 */

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Text;
using TvDatabase;
using TvLibrary.Epg;
using TvLibrary.Log;
using TvLibrary.Channels;

namespace TvDatabase
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
    public bool FitsInHole(DateTime start, DateTime end)
    {
      return (start >= this.start && end <= this.end);
    }
  }
  public class EpgHoleCollection : List<EpgHole>
  {
    public bool FitsInAnyHole(DateTime start, DateTime end)
    {
      foreach (EpgHole hole in this)
      {
        if (hole.FitsInHole(start, end))
          return true;
      }
      return false;
    }
  }

  public class EpgDBUpdater
  {
    #region Variables
    string _titleTemplate;
    string _descriptionTemplate;
    string _epgLanguages;
    string _grabberName;
    bool _storeOnlySelectedChannels;
    bool _checkForLastUpdate;
    int _epgReGrabAfter = 240;//4 hours
    bool _alwaysFillHoles;
    bool _alwaysReplace;
    TvBusinessLayer _layer;
    #endregion

    #region ctor
    public EpgDBUpdater(string grabberName, bool checkForLastUpdate)
    {
      _grabberName = grabberName;
      _checkForLastUpdate = checkForLastUpdate;
      ReloadConfig();
    }
    #endregion

    #region Public members
    public void ReloadConfig()
    {
      _layer = new TvBusinessLayer();
      _titleTemplate = _layer.GetSetting("epgTitleTemplate", "%TITLE%").Value;
      _descriptionTemplate = _layer.GetSetting("epgDescriptionTemplate", "%DESCRIPTION%").Value;
      _epgLanguages = _layer.GetSetting("epgLanguages").Value;
      Setting setting = _layer.GetSetting("epgStoreOnlySelected");
      _storeOnlySelectedChannels = (setting.Value == "yes");
      Setting s = _layer.GetSetting("timeoutEPGRefresh", "240");
      if (Int32.TryParse(s.Value, out _epgReGrabAfter) == false)
      {
        _epgReGrabAfter = 240;
      }
      _alwaysFillHoles = (_layer.GetSetting("generalEPGAlwaysFillHoles", "no").Value == "yes");
      _alwaysReplace = (_layer.GetSetting("generalEPGAlwaysReplace", "no").Value == "yes");
    }
    public void UpdateEpgForChannel(EpgChannel epgChannel)
    {
      int iInserted = 0;
      bool hasGaps = false;
      Channel dbChannel = IsInsertAllowed(epgChannel);
      if (dbChannel == null)
        return;
      Log.Epg("{0}: {1} lastUpdate:{2}", _grabberName, dbChannel.DisplayName, dbChannel.LastGrabTime);
      _layer.RemoveOldPrograms(dbChannel.IdChannel);

      EpgHoleCollection holes = new EpgHoleCollection();
      if ((dbChannel.EpgHasGaps || _alwaysFillHoles) && !_alwaysReplace)
      {
        Log.Epg("{0}: {1} is marked to have epg gaps. Calculating them...", _grabberName, dbChannel.DisplayName);
        IList infos = _layer.GetPrograms(dbChannel, DateTime.Now);
        if (infos.Count > 1)
        {
          for (int i = 1; i < infos.Count; i++)
          {
            Program prev = (Program)infos[i - 1];
            Program current = (Program)infos[i];
            TimeSpan diff=current.StartTime-prev.EndTime;
            if (diff.TotalMinutes > 5)
              holes.Add(new EpgHole(prev.EndTime, current.StartTime));
          }
        }
        Log.Epg("{0}: {1} Found {2} epg holes.", _grabberName, dbChannel.DisplayName,holes.Count);
      }
      DateTime dbLastProgram = _layer.GetNewestProgramForChannel(dbChannel.IdChannel);
      EpgProgram lastProgram = null;
      for (int i = 0; i < epgChannel.Programs.Count; i++)
      {
        EpgProgram epgProgram = epgChannel.Programs[i];
        // Check for dupes
        if (lastProgram != null)
        {
          if (epgProgram.StartTime == lastProgram.StartTime && epgProgram.EndTime == lastProgram.EndTime)
            continue;
          TimeSpan diff = epgProgram.StartTime - lastProgram.EndTime;
          if (diff.Minutes > 5)
            hasGaps = true;
        }
        if (epgProgram.StartTime <= dbLastProgram && !_alwaysReplace)
        {
          if (epgProgram.StartTime < DateTime.Now) continue;
          if (!holes.FitsInAnyHole(epgProgram.StartTime, epgProgram.EndTime)) continue;
          Log.Epg("{0}: Great we stuffed an epg hole {1}-{2} :-)", _grabberName, epgProgram.StartTime.ToShortDateString()+" "+epgProgram.StartTime.ToShortTimeString(), epgProgram.EndTime.ToShortDateString()+" "+epgProgram.EndTime.ToShortTimeString());
        }
        TvDatabase.Program prog = null;
        if (_alwaysReplace)
        {
          IList epgs = _layer.GetProgramExists(dbChannel, epgProgram.StartTime, epgProgram.EndTime);
          if (epgs.Count > 0)
            prog = (TvDatabase.Program)epgs[0];
        }
        AddProgramAndApplyTemplates(dbChannel, epgProgram, prog);
        iInserted++;
        lastProgram = epgProgram;
      }
      dbChannel.LastGrabTime = DateTime.Now;
      dbChannel.EpgHasGaps = hasGaps;
      dbChannel.Persist();
      Log.Epg("- Inserted {0} epg entries for channel {1}", iInserted, dbChannel.DisplayName);
    }
    #endregion

    #region Private functions
    private Channel IsInsertAllowed(EpgChannel epgChannel)
    {
      DVBBaseChannel dvbChannel = (DVBBaseChannel)epgChannel.Channel;
      //are there any epg infos for this channel?
      if (epgChannel.Programs.Count == 0)
      {
        Log.Epg("{0}: no epg infos found for channel networkid:0x{1:X} transportid:0x{2:X} serviceid:0x{3:X}", _grabberName, dvbChannel.NetworkId, dvbChannel.TransportId, dvbChannel.ServiceId);
        return null;
      }
      //do we know a channel with these tuning details?
      Channel dbChannel = _layer.GetChannelByTuningDetail(dvbChannel.NetworkId, dvbChannel.TransportId, dvbChannel.ServiceId);
      if (dbChannel == null)
      {
        Log.Epg("{0}: no channel found for networkid:0x{1:X} transportid:0x{2:X} serviceid:0x{3:X}", _grabberName, dvbChannel.NetworkId, dvbChannel.TransportId, dvbChannel.ServiceId);
        foreach (EpgProgram ei in epgChannel.Programs)
        {
          string title = "";
          if (ei.Text.Count > 0)
            title = ei.Text[0].Title;
          Log.Epg("                   -> {0}-{1}  {2}", ei.StartTime, ei.EndTime, title);
        }
        return null;
      }
      //should we store epg for this channel?
      if (_storeOnlySelectedChannels)
      {
        if (!dbChannel.GrabEpg)
        {
          Log.Epg("{0}: channel {1} is not configured to grab epg.", _grabberName, dbChannel.DisplayName);
          return null;
        }
      }
      if (_checkForLastUpdate)
      {
        //is the regrab time reached?
        TimeSpan ts = DateTime.Now - dbChannel.LastGrabTime;
        if (ts.TotalMinutes < _epgReGrabAfter)
        {

          Log.Epg("{0}: {1} not needed lastUpdate:{2}", _grabberName, dbChannel.DisplayName, dbChannel.LastGrabTime);
          return null;
        }
      }
      return dbChannel;
    }

    #region Template Tools
    private string GetStarRatingStr(int starRating)
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
    private string EvalTemplate(string template, NameValueCollection values)
    {
      for (int i = 0; i < values.Count; i++)
        template = template.Replace(values.Keys[i], values[i]);
      return template;
    }
    #endregion

    #region Single Program Updating Tools
    private void GetEPGLanguage(List<EpgLanguageText> texts, out string title, out string description, out string genre, out int starRating, out string classification, out int parentalRating)
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
          if (texts[0].Language.ToLower() == "all")
          {
            offset = i;
            break;
          }
          if (_epgLanguages.Length == 0 || _epgLanguages.ToLower().IndexOf(texts[i].Language.ToLower()) >= 0)
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

      if (title == null) title = "";
      if (description == null) description = "";
      if (genre == null) genre = "";
      if (classification == null) classification = "";
    }
    private void AddProgramAndApplyTemplates(Channel dbChannel, EpgProgram ep,TvDatabase.Program dbProg)
    {
      string title; string description; string genre; int starRating; string classification; int parentRating;
      GetEPGLanguage(ep.Text, out title, out description, out genre, out starRating, out classification, out parentRating);
      NameValueCollection values = new NameValueCollection();
      values.Add("%TITLE%", title);
      values.Add("%DESCRIPTION%", description);
      values.Add("%GENRE%", genre);
      values.Add("%STARRATING%", starRating.ToString());
      values.Add("%STARRATING_STR%", GetStarRatingStr(starRating));
      values.Add("%CLASSIFICATION%", classification);
      values.Add("%PARENTALRATING%", parentRating.ToString());
      values.Add("%NEWLINE%", Environment.NewLine);
      title=EvalTemplate(_titleTemplate, values);
      description=EvalTemplate(_descriptionTemplate, values);
      if (dbProg==null)
        dbProg = new TvDatabase.Program(dbChannel.IdChannel, ep.StartTime, ep.EndTime, title, description, genre, false, System.Data.SqlTypes.SqlDateTime.MinValue, string.Empty, string.Empty, starRating, classification, parentRating);
      else
      {
        dbProg.Title=title;
        dbProg.Description=description;
        dbProg.Genre=genre;
        dbProg.StarRating=starRating;
        dbProg.Classification=classification;
        dbProg.ParentalRating=parentRating;
      }
      dbProg.Persist();
    }
    #endregion
    #endregion
  }
}
