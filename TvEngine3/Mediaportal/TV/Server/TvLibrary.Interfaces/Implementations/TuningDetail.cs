﻿#region Copyright (C) 2005-2011 Team MediaPortal

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
using System.Xml.Serialization;
using Mediaportal.TV.Server.Common.Types.Country;
using Mediaportal.TV.Server.Common.Types.Enum;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Channel;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Implementations.Channel;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Logging;
using MediaPortal.Common.Utils.ExtensionMethods;

namespace Mediaportal.TV.Server.TVLibrary.Interfaces.Implementations.TuningDetail
{
  [Serializable]
  public class TuningDetail
  {
    public BroadcastStandard BroadcastStandard = BroadcastStandard.Unknown;
    public short SatellitePosition = -1;
    public int CellId = -1;
    public int CellIdExtension = -1;
    public int Frequency = -1;      // unit = kHz
    public int FrequencyOffset = 0; // unit = kHz
    public short PhysicalChannelNumber = -1;
    public int Bandwidth = -1;      // unit = kHz
    public Polarisation Polarisation = Polarisation.Automatic;
    public string ModulationScheme = "Automatic";
    public int SymbolRate = -1;     // unit = ks/s
    public FecCodeRate FecCodeRate = FecCodeRate.Automatic;
    public PilotTonesState PilotTonesState = PilotTonesState.Automatic;
    public RollOffFactor RollOffFactor = RollOffFactor.Automatic;
    public int StreamId = -1;

    // Placeholders, to enable this class to be used for satellite, analog TV
    // and stream scanning even though we don't load the tuning details from
    // file.
    // TODO remove all satellite attributes except a satellite longitude/ID
    [XmlIgnore]
    public ILnbType LnbType;
    [XmlIgnore]
    public DiseqcPort DiseqcPort;
    [XmlIgnore]
    public Country Country;
    [XmlIgnore]
    public AnalogTunerSource TunerSource;
    [XmlIgnore]
    public string Url;

    [XmlIgnore]
    public ModulationSchemeVsb ModulationSchemeVsb
    {
      get
      {
        ModulationSchemeVsb vsbModulation;
        if (ModulationScheme != null && Enum.TryParse<ModulationSchemeVsb>(ModulationScheme, out vsbModulation))
        {
          return vsbModulation;
        }
        return ModulationSchemeVsb.Automatic;
      }
    }

    [XmlIgnore]
    public ModulationSchemeQam ModulationSchemeQam
    {
      get
      {
        ModulationSchemeQam qamModulation;
        if (ModulationScheme != null && Enum.TryParse<ModulationSchemeQam>(ModulationScheme, out qamModulation))
        {
          return qamModulation;
        }
        return ModulationSchemeQam.Automatic;
      }
    }

    [XmlIgnore]
    public ModulationSchemePsk ModulationSchemePsk
    {
      get
      {
        ModulationSchemePsk pskModulation;
        if (ModulationScheme != null && Enum.TryParse<ModulationSchemePsk>(ModulationScheme, out pskModulation))
        {
          return pskModulation;
        }
        return ModulationSchemePsk.Automatic;
      }
    }

    public IChannel GetTuningChannel()
    {
      IChannel channel = null;
      switch (BroadcastStandard)
      {
        case BroadcastStandard.AnalogTelevision:
          ChannelAnalogTv analogTvChannel = new ChannelAnalogTv();
          analogTvChannel.Country = Country;
          analogTvChannel.PhysicalChannelNumber = PhysicalChannelNumber;
          analogTvChannel.TunerSource = TunerSource;
          channel = analogTvChannel;
          break;
        case BroadcastStandard.Atsc:
          ChannelAtsc atscChannel = new ChannelAtsc();
          atscChannel.ModulationScheme = ModulationSchemeVsb;
          channel = atscChannel;
          break;
        case BroadcastStandard.DigiCipher2:
          channel = new ChannelDigiCipher2();
          break;
        case BroadcastStandard.DvbC:
          ChannelDvbC dvbcChannel = new ChannelDvbC();
          dvbcChannel.ModulationScheme = ModulationSchemeQam;
          dvbcChannel.SymbolRate = SymbolRate;
          channel = dvbcChannel;
          break;
        case BroadcastStandard.DvbC2:
          ChannelDvbC2 dvbc2Channel = new ChannelDvbC2();
          dvbc2Channel.PlpId = (short)StreamId;
          channel = dvbc2Channel;
          break;
        case BroadcastStandard.DvbIp:
          ChannelStream streamChannel = new ChannelStream();
          streamChannel.Url = Url;
          channel = streamChannel;
          break;
        case BroadcastStandard.DvbS:
          channel = new ChannelDvbS();
          break;
        case BroadcastStandard.DvbS2:
          ChannelDvbS2 dvbs2Channel = new ChannelDvbS2();
          dvbs2Channel.PilotTonesState = PilotTonesState;
          dvbs2Channel.RollOffFactor = RollOffFactor;
          dvbs2Channel.StreamId = (short)StreamId;
          channel = dvbs2Channel;
          break;
        case BroadcastStandard.DvbT:
          channel = new ChannelDvbT();
          break;
        case BroadcastStandard.DvbT2:
          ChannelDvbT2 dvbt2Channel = new ChannelDvbT2();
          dvbt2Channel.PlpId = (short)StreamId;
          channel = dvbt2Channel;
          break;
        case BroadcastStandard.ExternalInput:
          channel = new ChannelCapture();
          break;
        case BroadcastStandard.FmRadio:
          channel = new ChannelFmRadio();
          break;
        case BroadcastStandard.SatelliteTurboFec:
          channel = new ChannelSatelliteTurboFec();
          break;
        case BroadcastStandard.Scte:
          ChannelScte scteChannel = new ChannelScte();
          scteChannel.ModulationScheme = ModulationSchemeQam;
          channel = scteChannel;
          break;
        default:
          this.LogError("tuning detail: failed to handle broadcast standard {0} in GetTuningChannel()", BroadcastStandard);
          return null;
      }

      IChannelOfdm ofdmChannel = channel as IChannelOfdm;
      if (ofdmChannel != null)
      {
        ofdmChannel.Bandwidth = Bandwidth;
      }

      IChannelPhysical physicalChannel = channel as IChannelPhysical;
      if (physicalChannel != null)
      {
        physicalChannel.Frequency = Frequency;
      }

      IChannelSatellite satelliteChannel = channel as IChannelSatellite;
      if (satelliteChannel != null)
      {
        satelliteChannel.DiseqcPositionerSatelliteIndex = -1;   // TODO this disables motor support for now, FIX!
        satelliteChannel.LnbType = LnbType;
        satelliteChannel.DiseqcSwitchPort = DiseqcPort;
        satelliteChannel.Polarisation = Polarisation;
        satelliteChannel.ModulationScheme = ModulationSchemePsk;
        satelliteChannel.SymbolRate = SymbolRate;
        satelliteChannel.FecCodeRate = FecCodeRate;
      }

      return channel;
    }

    #region object overrides

    /// <summary>
    /// Determine whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
    /// </summary>
    /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
    /// <returns><c>true</c> if the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>, otherwise <c>false</c></returns>
    public override bool Equals(object obj)
    {
      TuningDetail tuningDetail = obj as TuningDetail;
      if (
        tuningDetail == null ||
        BroadcastStandard != tuningDetail.BroadcastStandard ||
        SatellitePosition != tuningDetail.SatellitePosition ||
        CellId != tuningDetail.CellId ||
        CellIdExtension != tuningDetail.CellIdExtension ||
        Frequency != tuningDetail.Frequency ||
        FrequencyOffset != tuningDetail.FrequencyOffset ||
        Bandwidth != tuningDetail.Bandwidth ||
        Polarisation != tuningDetail.Polarisation ||
        !string.Equals(ModulationScheme, tuningDetail.ModulationScheme) ||
        SymbolRate != tuningDetail.SymbolRate ||
        FecCodeRate != tuningDetail.FecCodeRate ||
        PilotTonesState != tuningDetail.PilotTonesState ||
        RollOffFactor != tuningDetail.RollOffFactor ||
        StreamId != tuningDetail.StreamId
      )
      {
        return false;
      }
      return true;
    }

    /// <summary>
    /// A hash function for this type.
    /// </summary>
    /// <returns>a hash code for the current <see cref="T:System.Object"/></returns>
    public override int GetHashCode()
    {
      return base.GetHashCode() ^ BroadcastStandard.GetHashCode() ^
              SatellitePosition.GetHashCode() ^ CellId.GetHashCode() ^
              CellIdExtension.GetHashCode() ^ Frequency.GetHashCode() ^
              FrequencyOffset.GetHashCode() ^ Bandwidth.GetHashCode() ^
              Polarisation.GetHashCode() ^ ModulationScheme.GetHashCode() ^
              SymbolRate.GetHashCode() ^ FecCodeRate.GetHashCode() ^
              PilotTonesState.GetHashCode() ^ RollOffFactor.GetHashCode() ^
              StreamId.GetHashCode();
    }

    /// <summary>
    /// Get a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
    /// </summary>
    /// <returns>a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/></returns>
    public override string ToString()
    {
      string frequencyMhz = string.Format("{0:#.##}", (float)Frequency / 1000);
      switch (BroadcastStandard)
      {
        case BroadcastStandard.AmRadio:
          return string.Format("{0} kHz", Frequency);
        case BroadcastStandard.AnalogTelevision:
          return string.Format("#{0}", PhysicalChannelNumber);
        case BroadcastStandard.FmRadio:
          return string.Format("{0} MHz", frequencyMhz);
        case BroadcastStandard.ExternalInput:
          return "external inputs";
        case BroadcastStandard.DvbC:
          return string.Format("{0} MHz, {1}, {2} ks/s", frequencyMhz, ModulationSchemeQam.GetDescription(), SymbolRate);
        case BroadcastStandard.DvbC2:
        case BroadcastStandard.DvbT:
        case BroadcastStandard.DvbT2:
          string offsetDescription = string.Empty;
          if (FrequencyOffset > 0)
          {
            offsetDescription = string.Format(" (+/- {0} kHz)", FrequencyOffset);
          }
          string plpIdDescription = string.Empty;
          if (BroadcastStandard != BroadcastStandard.DvbT && StreamId >= 0)
          {
            plpIdDescription = string.Format(", PLP {0}", StreamId);
          }
          return string.Format("{0} {1} MHz{2}, BW {3:#.##} MHz{4}", BroadcastStandard.GetDescription(), frequencyMhz, offsetDescription, (float)Bandwidth / 1000, plpIdDescription);
        case BroadcastStandard.DvbIp:
          return Url;
        case BroadcastStandard.DvbDsng:
        case BroadcastStandard.DvbS:
        case BroadcastStandard.DvbS2:
        case BroadcastStandard.DvbS2X:
        case BroadcastStandard.SatelliteTurboFec:
        case BroadcastStandard.DigiCipher2:
          return string.Format("{0} {1} MHz, {2}, {3}, {4} ks/s", BroadcastStandard.GetDescription(), frequencyMhz, Polarisation.GetDescription(), ModulationSchemePsk.GetDescription(), SymbolRate);
        case BroadcastStandard.Atsc:
          return string.Format("{0} MHz (#{1}), {2}", frequencyMhz, ChannelAtsc.GetPhysicalChannelNumberForFrequency(Frequency), ModulationSchemeVsb.GetDescription());
        case BroadcastStandard.Scte:
          if (Frequency <= 0)
          {
            return "CableCARD out-of-band SI";
          }
          return string.Format("{0} MHz (#{1}), {2}", frequencyMhz, ChannelScte.GetPhysicalChannelNumberForFrequency(Frequency), ModulationSchemeQam.GetDescription());

        // Not implemented.
        case BroadcastStandard.IsdbC:
        case BroadcastStandard.IsdbS:
        case BroadcastStandard.IsdbT:
        case BroadcastStandard.DirecTvDss:
        case BroadcastStandard.Dab:
        default:
          return string.Empty;
      }
    }

    #endregion
  }
}