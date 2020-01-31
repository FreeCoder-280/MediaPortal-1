#region Copyright (C) 2005-2020 Team MediaPortal

// Copyright (C) 2005-2020 Team MediaPortal
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
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using SQLite.NET;

using MediaPortal.Configuration;
using MediaPortal.Database;
using MediaPortal.GUI.Library;
using MediaPortal.GUI.Pictures;
using MediaPortal.Player;
using MediaPortal.Util;

namespace MediaPortal.Picture.Database
{
  /// <summary>
  /// Summary description for PictureDatabaseSqlLite.
  /// </summary>
  public class PictureDatabaseSqlLite : IPictureDatabase, IDisposable
  {
    private bool disposed = false;
    private SQLiteClient m_db = null;
    private bool _useExif = true;
    private bool _usePicasa = false;
    private bool _dbHealth = false;

    public PictureDatabaseSqlLite()
    {
      Open();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private void Open()
    {
      try
      {
        // Maybe called by an exception
        if (m_db != null)
        {
          try
          {
            m_db.Close();
            m_db.Dispose();
            m_db = null;
            Log.Warn("Picture.DB.SQLite: Disposing current DB instance..");
          }
          catch (Exception ex)
          {
            Log.Error("Picture.DB.SQLite: Open: {0}", ex.Message);
          }
        }

        // Open database
        try
        {
          Directory.CreateDirectory(Config.GetFolder(Config.Dir.Database));
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Create DB directory: {0}", ex.Message);
        }

        m_db = new SQLiteClient(Config.GetFile(Config.Dir.Database, "PictureDatabaseV3.db3"));
        // Retry 10 times on busy (DB in use or system resources exhausted)
        m_db.BusyRetries = 10;
        // Wait 100 ms between each try (default 10)
        m_db.BusyRetryDelay = 100;

        _dbHealth = DatabaseUtility.IntegrityCheck(m_db);

        DatabaseUtility.SetPragmas(m_db);
        m_db.Execute("PRAGMA foreign_keys=ON");

        CreateTables();
        InitSettings();
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: Open DB: {0} stack:{1}", ex.Message, ex.StackTrace);
        Open();
      }
      Log.Info("Picture database opened...");
    }

    private void InitSettings()
    {
      using (Profile.Settings xmlreader = new Profile.MPSettings())
      {
        _useExif = xmlreader.GetValueAsBool("pictures", "useExif", true);
        _usePicasa = xmlreader.GetValueAsBool("pictures", "usePicasa", false);
      }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private bool CreateTables()
    {
      if (m_db == null)
      {
        return false;
      }

      #region Tables
      DatabaseUtility.AddTable(m_db, "picture",
                               "CREATE TABLE picture (idPicture INTEGER PRIMARY KEY, strFile TEXT, iRotation INTEGER, strDateTaken TEXT, " +
                                                      "iImageWidth INTEGER, iImageHeight INTEGER, " +
                                                      "iImageXReso INTEGER, iImageYReso INTEGER);");
      #endregion

      #region Indexes
      DatabaseUtility.AddIndex(m_db, "idxpicture_idPicture", "CREATE INDEX idxpicture_idPicture ON picture(idPicture)");
      DatabaseUtility.AddIndex(m_db, "idxpicture_strFile", "CREATE INDEX idxpicture_strFile ON picture (strFile ASC)");
      DatabaseUtility.AddIndex(m_db, "idxpicture_strDateTaken", "CREATE INDEX idxpicture_strDateTaken ON picture (strDateTaken ASC)");
      DatabaseUtility.AddIndex(m_db, "idxpicture_strDateTaken_Year", "CREATE INDEX idxpicture_strDateTaken_Year ON picture (SUBSTR(strDateTaken,1,4))");
      DatabaseUtility.AddIndex(m_db, "idxpicture_strDateTaken_Month", "CREATE INDEX idxpicture_strDateTaken_Month ON picture (SUBSTR(strDateTaken,6,2))");
      DatabaseUtility.AddIndex(m_db, "idxpicture_strDateTaken_Day", "CREATE INDEX idxpicture_strDateTaken_Day ON picture (SUBSTR(strDateTaken,9,2))");
      #endregion

      #region Exif Tables
      DatabaseUtility.AddTable(m_db, "camera",
                               "CREATE TABLE camera (idCamera INTEGER PRIMARY KEY, strCamera TEXT, strCameraMake TEXT);");
      DatabaseUtility.AddTable(m_db, "lens",
                               "CREATE TABLE lens (idLens INTEGER PRIMARY KEY, strLens TEXT, strLensMake TEXT);");
      DatabaseUtility.AddTable(m_db, "orientation",
                               "CREATE TABLE orientation (idOrientation INTEGER PRIMARY KEY, strOrientation TEXT);");
      DatabaseUtility.AddTable(m_db, "flash",
                               "CREATE TABLE flash (idFlash INTEGER PRIMARY KEY, strFlash TEXT);");
      DatabaseUtility.AddTable(m_db, "meteringmode",
                               "CREATE TABLE meteringmode (idMeteringMode INTEGER PRIMARY KEY, strMeteringMode TEXT);");
      DatabaseUtility.AddTable(m_db, "country",
                               "CREATE TABLE country (idCountry INTEGER PRIMARY KEY, strCountryCode TEXT, strCountry TEXT);");
      DatabaseUtility.AddTable(m_db, "state",
                               "CREATE TABLE state (idState INTEGER PRIMARY KEY, strState TEXT);");
      DatabaseUtility.AddTable(m_db, "city",
                               "CREATE TABLE city (idCity INTEGER PRIMARY KEY, strCity TEXT);");
      DatabaseUtility.AddTable(m_db, "sublocation",
                               "CREATE TABLE sublocation (idSublocation INTEGER PRIMARY KEY, strSublocation TEXT);");
      DatabaseUtility.AddTable(m_db, "exposureprogram",
                               "CREATE TABLE exposureprogram (idExposureProgram INTEGER PRIMARY KEY, strExposureProgram TEXT);");
      DatabaseUtility.AddTable(m_db, "exposuremode",
                               "CREATE TABLE exposuremode (idExposureMode INTEGER PRIMARY KEY, strExposureMode TEXT);");
      DatabaseUtility.AddTable(m_db, "sensingmethod",
                               "CREATE TABLE sensingmethod (idSensingMethod INTEGER PRIMARY KEY, strSensingMethod TEXT);");
      DatabaseUtility.AddTable(m_db, "scenetype",
                               "CREATE TABLE scenetype (idSceneType INTEGER PRIMARY KEY, strSceneType TEXT);");
      DatabaseUtility.AddTable(m_db, "scenecapturetype",
                               "CREATE TABLE scenecapturetype (idSceneCaptureType INTEGER PRIMARY KEY, strSceneCaptureType TEXT);");
      DatabaseUtility.AddTable(m_db, "whitebalance",
                               "CREATE TABLE whitebalance (idWhiteBalance INTEGER PRIMARY KEY, strWhiteBalance TEXT);");
      DatabaseUtility.AddTable(m_db, "author",
                               "CREATE TABLE author (idAuthor INTEGER PRIMARY KEY, strAuthor TEXT);");
      DatabaseUtility.AddTable(m_db, "byline",
                               "CREATE TABLE byline (idByline INTEGER PRIMARY KEY, strByline TEXT);");
      DatabaseUtility.AddTable(m_db, "software",
                               "CREATE TABLE software (idSoftware INTEGER PRIMARY KEY, strSoftware TEXT);");
      DatabaseUtility.AddTable(m_db, "usercomment",
                               "CREATE TABLE usercomment (idUserComment INTEGER PRIMARY KEY, strUserComment TEXT);");
      DatabaseUtility.AddTable(m_db, "copyright",
                               "CREATE TABLE copyright (idCopyright INTEGER PRIMARY KEY, strCopyright TEXT);");
      DatabaseUtility.AddTable(m_db, "copyrightnotice",
                               "CREATE TABLE copyrightnotice (idCopyrightNotice INTEGER PRIMARY KEY, strCopyrightNotice TEXT);");

      DatabaseUtility.AddTable(m_db, "iso",
                               "CREATE TABLE iso (idISO INTEGER PRIMARY KEY, strISO TEXT);");
      DatabaseUtility.AddTable(m_db, "exposureTime",
                               "CREATE TABLE exposureTime (idExposureTime INTEGER PRIMARY KEY, strExposureTime TEXT);");
      DatabaseUtility.AddTable(m_db, "exposureCompensation",
                               "CREATE TABLE exposureCompensation (idExposureCompensation INTEGER PRIMARY KEY, strExposureCompensation TEXT);");
      DatabaseUtility.AddTable(m_db, "fStop",
                               "CREATE TABLE fStop (idFStop INTEGER PRIMARY KEY, strFStop TEXT);");
      DatabaseUtility.AddTable(m_db, "shutterSpeed",
                               "CREATE TABLE shutterSpeed (idShutterSpeed INTEGER PRIMARY KEY, strShutterSpeed TEXT);");
      DatabaseUtility.AddTable(m_db, "focalLength",
                               "CREATE TABLE focalLength (idFocalLength INTEGER PRIMARY KEY, strFocalLength TEXT);");
      DatabaseUtility.AddTable(m_db, "focalLength35mm",
                               "CREATE TABLE focalLength35mm (idFocalLength35mm INTEGER PRIMARY KEY, strFocalLength35mm TEXT);");

      DatabaseUtility.AddTable(m_db, "keywords",
                               "CREATE TABLE keywords (idKeyword INTEGER PRIMARY KEY, strKeyword TEXT);");
      DatabaseUtility.AddTable(m_db, "keywordslinkpicture",
                               "CREATE TABLE keywordslinkpicture (idKeyword INTEGER REFERENCES keywords(idKeyword) ON DELETE CASCADE, idPicture INTEGER REFERENCES picture(idPicture) ON DELETE CASCADE);");

      DatabaseUtility.AddTable(m_db, "exiflinkpicture",
                               "CREATE TABLE exiflinkpicture (idPicture INTEGER REFERENCES picture(idPicture) ON DELETE CASCADE, " +
                                                       "idCamera INTEGER REFERENCES camera(idCamera) ON DELETE CASCADE, " +
                                                       "idLens INTEGER REFERENCES lens(idLens) ON DELETE CASCADE, " +
                                                       "idISO INTEGER REFERENCES iso(idIso) ON DELETE CASCADE, " +
                                                       "idExposureTime INTEGER REFERENCES exposureTime(idExposureTime) ON DELETE CASCADE, " +
                                                       "idExposureCompensation INTEGER REFERENCES exposureCompensation(idExposureCompensation) ON DELETE CASCADE, " +
                                                       "idFStop INTEGER REFERENCES fStop(idFStop) ON DELETE CASCADE, " +
                                                       "idShutterSpeed INTEGER REFERENCES shutterSpeed(idShutterSpeed) ON DELETE CASCADE, " +
                                                       "idFocalLength INTEGER REFERENCES focalLength(idFocalLength) ON DELETE CASCADE, " +
                                                       "idFocalLength35mm INTEGER REFERENCES focalLength35mm(idFocalLength35mm) ON DELETE CASCADE, " +
                                                       "strGPSLatitude TEXT, strGPSLongitude TEXT, strGPSAltitude TEXT, " +
                                                       "idOrientation INTEGER REFERENCES orientation(idOrientation) ON DELETE CASCADE, " +
                                                       "idFlash INTEGER REFERENCES flash(idFlash) ON DELETE CASCADE, " +
                                                       "idMeteringMode INTEGER REFERENCES meteringmode(idMeteringMode) ON DELETE CASCADE, " +
                                                       "idExposureProgram INTEGER REFERENCES exposureprogram(idExposureProgram) ON DELETE CASCADE, " +
                                                       "idExposureMode INTEGER REFERENCES exposuremode(idExposureMode) ON DELETE CASCADE, " +
                                                       "idSensingMethod INTEGER REFERENCES sensingmethod(idSensingMethod) ON DELETE CASCADE, " +
                                                       "idSceneType INTEGER REFERENCES scenetype(idSceneType) ON DELETE CASCADE, " +
                                                       "idSceneCaptureType INTEGER REFERENCES scenecapturetype(idSceneCaptureType) ON DELETE CASCADE, " +
                                                       "idWhiteBalance INTEGER REFERENCES whitebalance(idWhiteBalance) ON DELETE CASCADE," +
                                                       "idAuthor INTEGER REFERENCES author(idAuthor) ON DELETE CASCADE, " +
                                                       "idByline INTEGER REFERENCES byline(idByline) ON DELETE CASCADE, " +
                                                       "idSoftware INTEGER REFERENCES software(idSoftware) ON DELETE CASCADE, " +
                                                       "idUserComment INTEGER REFERENCES usercomment(idUserComment) ON DELETE CASCADE, " +
                                                       "idCopyright INTEGER REFERENCES copyright(idCopyright) ON DELETE CASCADE, " +
                                                       "idCopyrightNotice INTEGER REFERENCES copyrightnotice(idCopyrightNotice) ON DELETE CASCADE, " +
                                                       "idCountry INTEGER REFERENCES country(idCountry) ON DELETE CASCADE, " +
                                                       "idState INTEGER REFERENCES state(idState) ON DELETE CASCADE, " +
                                                       "idCity INTEGER REFERENCES city(idCity) ON DELETE CASCADE, " +
                                                       "idSublocation INTEGER REFERENCES sublocation(idSublocation) ON DELETE CASCADE);");
      #endregion

      #region Exif Indexes
      DatabaseUtility.AddIndex(m_db, "idxcamera_idCamera", "CREATE INDEX idxcamera_idCamera ON camera(idCamera);");
      DatabaseUtility.AddIndex(m_db, "idxlens_idLens", "CREATE INDEX idxlens_idLens ON lens(idLens);");
      DatabaseUtility.AddIndex(m_db, "idxorientation_idOrientation", "CREATE INDEX idxorientation_idOrientation ON orientation(idOrientation);");
      DatabaseUtility.AddIndex(m_db, "idxflash_idFlash", "CREATE INDEX idxflash_idFlash ON flash(idFlash);");
      DatabaseUtility.AddIndex(m_db, "idxmeteringmode_idMeteringMode", "CREATE INDEX idxmeteringmode_idMeteringMode ON meteringmode(idMeteringMode);");
      DatabaseUtility.AddIndex(m_db, "idxcountry_idCountry", "CREATE INDEX idxcountry_idCountry ON country(idCountry);");
      DatabaseUtility.AddIndex(m_db, "idxstate_idState", "CREATE INDEX idxstate_idState ON state(idState);");
      DatabaseUtility.AddIndex(m_db, "idxcity_idCity", "CREATE INDEX idxcity_idCity ON city(idCity);");
      DatabaseUtility.AddIndex(m_db, "idxsublocation_idSublocation", "CREATE INDEX idxsublocation_idSublocation ON sublocation(idSublocation);");
      DatabaseUtility.AddIndex(m_db, "idxexposureprogram_idExposureProgram", "CREATE INDEX idxexposureprogram_idExposureProgram ON exposureprogram(idExposureProgram);");
      DatabaseUtility.AddIndex(m_db, "idxexposuremode_idExposureMode", "CREATE INDEX idxexposuremode_idExposureMode ON exposuremode(idExposureMode);");
      DatabaseUtility.AddIndex(m_db, "idxsensingmethod_idSensingMethod", "CREATE INDEX idxsensingmethod_idSensingMethod ON sensingmethod(idSensingMethod);");
      DatabaseUtility.AddIndex(m_db, "idxscenetype_idSceneType", "CREATE INDEX idxscenetype_idSceneType ON scenetype(idSceneType);");
      DatabaseUtility.AddIndex(m_db, "idxscenecapturetype_idSceneCaptureType", "CREATE INDEX idxscenecapturetype_idSceneCaptureType ON scenecapturetype(idSceneCaptureType);");
      DatabaseUtility.AddIndex(m_db, "idxwhitebalance_idWhiteBalance", "CREATE INDEX idxwhitebalance_idWhiteBalance ON whitebalance(idWhiteBalance);");
      DatabaseUtility.AddIndex(m_db, "idxauthor_idAuthor", "CREATE INDEX idxauthor_idAuthor ON author(idAuthor);");
      DatabaseUtility.AddIndex(m_db, "idxbyline_idByline", "CREATE INDEX idxbyline_idByline ON byline(idByline);");
      DatabaseUtility.AddIndex(m_db, "idxsoftware_idSoftware", "CREATE INDEX idxsoftware_idSoftware ON software(idSoftware);");
      DatabaseUtility.AddIndex(m_db, "idxusercomment_idUserComment", "CREATE INDEX idxusercomment_idUserComment ON usercomment(idUserComment);");
      DatabaseUtility.AddIndex(m_db, "idxcopyright_idCopyright", "CREATE INDEX idxcopyright_idCopyright ON copyright(idCopyright);");
      DatabaseUtility.AddIndex(m_db, "idxcopyrightnotice_idCopyrightNotice", "CREATE INDEX idxcopyrightnotice_idCopyrightNotice ON copyrightnotice(idCopyrightNotice);");

      DatabaseUtility.AddIndex(m_db, "idxiso_idIso", "CREATE INDEX idxiso_idIso ON iso(idIso);");
      DatabaseUtility.AddIndex(m_db, "idxexposureTime_idExposureTime", "CREATE INDEX idxexposureTime_idExposureTime ON exposureTime(idExposureTime);");
      DatabaseUtility.AddIndex(m_db, "idxexposureCompensation_idExposureCompensation", "CREATE INDEX idxexposureCompensation_idExposureCompensation ON exposureCompensation(idExposureCompensation);");
      DatabaseUtility.AddIndex(m_db, "idxfStop_idFStop", "CREATE INDEX idxfStop_idFStop ON fStop(idFStop);");
      DatabaseUtility.AddIndex(m_db, "idxshutterSpeed_idShutterSpeed", "CREATE INDEX idxshutterSpeed_idShutterSpeed ON shutterSpeed(idShutterSpeed);");
      DatabaseUtility.AddIndex(m_db, "idxfocalLength_idFocalLength", "CREATE INDEX idxfocalLength_idFocalLength ON focalLength(idFocalLength);");
      DatabaseUtility.AddIndex(m_db, "idxfocalLength35mm_idFocalLength35mm", "CREATE INDEX idxfocalLength35mm_idFocalLength35mm ON focalLength35mm(idFocalLength35mm);");

      DatabaseUtility.AddIndex(m_db, "idxkeywords_idKeyword", "CREATE INDEX idxkeywords_idKeyword ON keywords(idKeyword);");
      DatabaseUtility.AddIndex(m_db, "idxkeywords_strKeyword", "CREATE INDEX idxkeywords_strKeyword ON keywords(strKeyword);");

      DatabaseUtility.AddIndex(m_db, "idxkeywordslinkpicture_idKeyword", "CREATE INDEX idxkeywordslinkpicture_idKeyword ON keywordslinkpicture(idKeyword);");
      DatabaseUtility.AddIndex(m_db, "idxkeywordslinkpicture_idPicture", "CREATE INDEX idxkeywordslinkpicture_idPicture ON keywordslinkpicture(idPicture);");

      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idPicture", "CREATE INDEX idxexiflinkpicture_idPicture ON exiflinkpicture(idPicture);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idCamera", "CREATE INDEX idxexiflinkpicture_idCamera ON exiflinkpicture(idCamera);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idLens", "CREATE INDEX idxexiflinkpicture_idLens ON exiflinkpicture(idLens);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idExif", "CREATE INDEX idxexiflinkpicture_idExif ON exiflinkpicture(idExif);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idOrientation", "CREATE INDEX idxexiflinkpicture_idOrientation ON exiflinkpicture(idOrientation);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idFlash", "CREATE INDEX idxexiflinkpicture_idFlash ON exiflinkpicture(idFlash);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idMeteringMode", "CREATE INDEX idxexiflinkpicture_idMeteringMode ON exiflinkpicture(idMeteringMode);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idExposureProgram", "CREATE INDEX idxexiflinkpicture_idExposureProgram ON exiflinkpicture(idExposureProgram);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idExposureMode", "CREATE INDEX idxexiflinkpicture_idExposureMode ON exiflinkpicture(idExposureMode);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idSensingMethod", "CREATE INDEX idxexiflinkpicture_idSensingMethod ON exiflinkpicture(idSensingMethod);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idSceneType", "CREATE INDEX idxexiflinkpicture_idSceneType ON exiflinkpicture(idSceneType);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idSceneCaptureType", "CREATE INDEX idxexiflinkpicture_idSceneCaptureType ON exiflinkpicture(idSceneCaptureType);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idWhiteBalance", "CREATE INDEX idxexiflinkpicture_idWhiteBalance ON exiflinkpicture(idWhiteBalance);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idAuthor", "CREATE INDEX idxexiflinkpicture_idAuthor ON exiflinkpicture(idAuthor);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idByline", "CREATE INDEX idxexiflinkpicture_idByline ON exiflinkpicture(idByline);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idSoftware", "CREATE INDEX idxexiflinkpicture_idSoftware ON exiflinkpicture(idSoftware);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idUserComment", "CREATE INDEX idxexiflinkpicture_idUserComment ON exiflinkpicture(idUserComment);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idCopyright", "CREATE INDEX idxexiflinkpicture_idCopyright ON exiflinkpicture(idCopyright);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idCopyrightNotice", "CREATE INDEX idxexiflinkpicture_idCopyrightNotice ON exiflinkpicture(idCopyrightNotice);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idCountry", "CREATE INDEX idxexiflinkpicture_idCountry ON exiflinkpicture(idCountry);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idState", "CREATE INDEX idxexiflinkpicture_idState ON exiflinkpicture(idState);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idCity", "CREATE INDEX idxexiflinkpicture_idCity ON exiflinkpicture(idCity);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idSublocation", "CREATE INDEX idxexiflinkpicture_idSublocation ON exiflinkpicture(idSublocation);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idIso", "CREATE INDEX idxexiflinkpicture_idIso ON exiflinkpicture(idIso);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idExposureTime", "CREATE INDEX idxexiflinkpictureidExposureTime ON exiflinkpicture(idExposureTime);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idExposureCompensation", "CREATE INDEX idxexiflinkpictureidExposureCompensation ON exiflinkpicture(idExposureCompensation);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idFStop", "CREATE INDEX idxexiflinkpictureidFStop ON exiflinkpicture(idFStop);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idShutterSpeed", "CREATE INDEX idxexiflinkpictureidShutterSpeed ON exiflinkpicture(idShutterSpeed);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idFocalLength", "CREATE INDEX idxexiflinkpictureidFocalLength ON exiflinkpicture(idFocalLength);");
      DatabaseUtility.AddIndex(m_db, "idxexiflinkpicture_idFocalLength35mm", "CREATE INDEX idxexiflinkpictureidFocalLength35mm ON exiflinkpicture(idFocalLength35mm);");

      #endregion

      #region Exif Triggers
      DatabaseUtility.AddTrigger(m_db, "Delete_ExtraData",
            "CREATE TRIGGER Delete_ExtraData AFTER DELETE ON exiflinkpicture " +
            "BEGIN " +
            "  DELETE FROM camera WHERE idCamera NOT IN (SELECT DISTINCT idCamera FROM exiflinkpicture); " +
            "  DELETE FROM lens WHERE idLens NOT IN (SELECT DISTINCT idLens FROM exiflinkpicture); " +
            "  DELETE FROM orientation WHERE idOrientation NOT IN (SELECT DISTINCT idOrientation FROM exiflinkpicture); " +
            "  DELETE FROM flash WHERE idFlash NOT IN (SELECT DISTINCT idFlash FROM exiflinkpicture); " +
            "  DELETE FROM meteringmode WHERE idMeteringMode NOT IN (SELECT DISTINCT idMeteringMode FROM exiflinkpicture); " +
            "  DELETE FROM exposureprogram WHERE idExposureProgram NOT IN (SELECT DISTINCT idExposureProgram FROM exiflinkpicture); " +
            "  DELETE FROM exposuremode WHERE idExposureMode NOT IN (SELECT DISTINCT idExposureMode FROM exiflinkpicture); " +
            "  DELETE FROM sensingmethod WHERE idSensingMethod NOT IN (SELECT DISTINCT idSensingMethod FROM exiflinkpicture); " +
            "  DELETE FROM scenetype WHERE idSceneType NOT IN (SELECT DISTINCT idSceneType FROM exiflinkpicture); " +
            "  DELETE FROM scenecapturetype WHERE idSceneCaptureType NOT IN (SELECT DISTINCT idSceneCaptureType FROM exiflinkpicture); " +
            "  DELETE FROM whitebalance WHERE idWhiteBalance NOT IN (SELECT DISTINCT idWhiteBalance FROM exiflinkpicture); " +
            "  DELETE FROM author WHERE idAuthor NOT IN (SELECT DISTINCT idAuthor FROM exiflinkpicture); " +
            "  DELETE FROM byline WHERE idByline NOT IN (SELECT DISTINCT idByline FROM exiflinkpicture); " +
            "  DELETE FROM software WHERE idSoftware NOT IN (SELECT DISTINCT idSoftware FROM exiflinkpicture); " +
            "  DELETE FROM usercomment WHERE idUserComment NOT IN (SELECT DISTINCT idUserComment FROM exiflinkpicture); " +
            "  DELETE FROM copyright WHERE idCopyright NOT IN (SELECT DISTINCT idCopyright FROM exiflinkpicture); " +
            "  DELETE FROM copyrightnotice WHERE idCopyrightNotice NOT IN (SELECT DISTINCT idCopyrightNotice FROM exiflinkpicture); " +
            "  DELETE FROM country WHERE idCountry NOT IN (SELECT DISTINCT idCountry FROM exiflinkpicture); " +
            "  DELETE FROM state WHERE idState NOT IN (SELECT DISTINCT idState FROM exiflinkpicture); " +
            "  DELETE FROM city WHERE idCity NOT IN (SELECT DISTINCT idCity FROM exiflinkpicture); " +
            "  DELETE FROM sublocation WHERE idSublocation NOT IN (SELECT DISTINCT idSublocation FROM exiflinkpicture); " +
            "  DELETE FROM iso WHERE idIso NOT IN (SELECT DISTINCT idIso FROM exiflinkpicture); " +
            "  DELETE FROM exposureTime WHERE idExposureTime NOT IN (SELECT DISTINCT idExposureTime FROM exiflinkpicture); " +
            "  DELETE FROM exposureCompensation WHERE idExposureCompensation NOT IN (SELECT DISTINCT idExposureCompensation FROM exiflinkpicture); " +
            "  DELETE FROM fStop WHERE idFStop NOT IN (SELECT DISTINCT idFStop FROM exiflinkpicture); " +
            "  DELETE FROM shutterSpeed WHERE idShutterSpeed NOT IN (SELECT DISTINCT idShutterSpeed FROM exiflinkpicture); " +
            "  DELETE FROM focalLength WHERE idFocalLength NOT IN (SELECT DISTINCT idFocalLength FROM exiflinkpicture); " +
            "  DELETE FROM focalLength35mm WHERE idFocalLength35mm NOT IN (SELECT DISTINCT idFocalLength35mm FROM exiflinkpicture); " +
            "END;");
      DatabaseUtility.AddTrigger(m_db, "Delete_ExtraKeywords",
            "CREATE TRIGGER Delete_ExtraKeywords AFTER DELETE ON keywordslinkpicture " +
            "BEGIN " +
            "  DELETE FROM keywords WHERE idKeyword NOT IN (SELECT DISTINCT idKeyword FROM keywordslinkpicture); " +
            "END;");
      #endregion

      #region Exif Views
      DatabaseUtility.AddView(m_db, "picturedata", "CREATE VIEW picturedata AS " +
                                                          "SELECT picture.idPicture, strDateTaken, iImageWidth, iImageHeight, iImageXReso, iImageYReso, " +
                                                          "strCamera, strCameraMake, strLens, strISO, strExposureTime, strExposureCompensation, strFStop, strShutterSpeed, " +
                                                          "strFocalLength, strFocalLength35mm, strGPSLatitude, strGPSLongitude, strGPSAltitude, " +
                                                          "exiflinkpicture.idOrientation, strOrientation, exiflinkpicture.idFlash, strFlash, exiflinkpicture.idMeteringMode, strMeteringMode, " +
                                                          "strCountryCode, strCountry, strState, strCity, strSubLocation, strExposureProgram, strExposureMode, strSensingMethod, strSceneType, " +
                                                          "strSceneCaptureType, strWhiteBalance, strAuthor, strByLine, strSoftware, strUserComment, strCopyright, strCopyrightNotice, " +
                                                          "iImageWidth||'x'||iImageHeight as strImageDimension, iImageXReso||'x'||iImageYReso as strImageResolution " +
                                                          "FROM picture " +
                                                          "LEFT JOIN exiflinkpicture ON picture.idPicture = exiflinkpicture.idPicture " +
                                                          "LEFT JOIN camera ON camera.idCamera = exiflinkpicture.idCamera " +
                                                          "LEFT JOIN lens ON lens.idLens = exiflinkpicture.idLens " +
                                                          "LEFT JOIN orientation ON orientation.idOrientation = exiflinkpicture.idOrientation " +
                                                          "LEFT JOIN flash ON flash.idFlash = exiflinkpicture.idFlash " +
                                                          "LEFT JOIN meteringmode ON meteringmode.idMeteringMode = exiflinkpicture.idMeteringMode " +
                                                          "LEFT JOIN country ON country.idCountry = exiflinkpicture.idCountry " +
                                                          "LEFT JOIN state ON state.idState = exiflinkpicture.idState " +
                                                          "LEFT JOIN city ON city.idCity = exiflinkpicture.idCity " +
                                                          "LEFT JOIN sublocation ON sublocation.idSublocation = exiflinkpicture.idSublocation " +
                                                          "LEFT JOIN exposureprogram ON exposureprogram.idExposureProgram = exiflinkpicture.idExposureProgram " +
                                                          "LEFT JOIN exposuremode ON exposuremode.idExposureMode = exiflinkpicture.idExposureMode " +
                                                          "LEFT JOIN sensingmethod ON sensingmethod.idSensingMethod = exiflinkpicture.idSensingMethod " +
                                                          "LEFT JOIN scenetype ON scenetype.idSceneType = exiflinkpicture.idSceneType " +
                                                          "LEFT JOIN scenecapturetype ON scenecapturetype.idSceneCaptureType = exiflinkpicture.idSceneCaptureType " +
                                                          "LEFT JOIN whitebalance ON whitebalance.idWhiteBalance = exiflinkpicture.idWhiteBalance " +
                                                          "LEFT JOIN author ON author.idAuthor = exiflinkpicture.idAuthor " +
                                                          "LEFT JOIN byline ON byline.idByline = exiflinkpicture.idByline " +
                                                          "LEFT JOIN software ON software.idSoftware = exiflinkpicture.idSoftware " +
                                                          "LEFT JOIN usercomment ON usercomment.idUserComment = exiflinkpicture.idUserComment " +
                                                          "LEFT JOIN copyright ON copyright.idCopyright = exiflinkpicture.idCopyright " +
                                                          "LEFT JOIN copyrightnotice ON copyrightnotice.idCopyrightNotice = exiflinkpicture.idCopyrightNotice " +
                                                          "LEFT JOIN iso ON iso.idISO = exiflinkpicture.idISO " +
                                                          "LEFT JOIN exposureTime ON exposureTime.idExposureTime = exiflinkpictureta.idExposureTime " +
                                                          "LEFT JOIN exposureCompensation ON exposureCompensation.idExposureCompensation = exiflinkpicture.idExposureCompensation " +
                                                          "LEFT JOIN fStop ON fStop.idFStop = exiflinkpicture.idFStop " +
                                                          "LEFT JOIN shutterSpeed ON shutterSpeed.idShutterSpeed = exiflinkpicture.idShutterSpeed " +
                                                          "LEFT JOIN focalLength ON focalLength.idFocalLength = exiflinkpicture.idFocalLength " +
                                                          "LEFT JOIN focalLength35mm ON focalLength35mm.idFocalLength35mm = exiflinkpicture.idFocalLength35mm;");

      DatabaseUtility.AddView(m_db, "picturekeywords", "CREATE VIEW picturekeywords AS " +
                                                       "SELECT picture.*, keywords.strKeyword FROM picture " +
                                                       "JOIN keywordslinkpicture ON picture.idPicture = keywordslinkpicture.idPicture " +
                                                       "JOIN keywords ON keywordslinkpicture.idKeyword = keywords.idKeyword;");
      #endregion

      return true;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public int AddPicture(string strPicture, int iRotation)
    {
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        return -1;
      }
      if (m_db == null)
      {
        return -1;
      }

      try
      {
        int lPicId = -1;
        string strPic = strPicture;
        string strDateTaken = string.Empty;

        DatabaseUtility.RemoveInvalidChars(ref strPic);
        string strSQL = String.Format("SELECT * FROM picture WHERE strFile LIKE '{0}'", strPic);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results != null && results.Rows.Count > 0)
        {
          lPicId = Int32.Parse(DatabaseUtility.Get(results, 0, "idPicture"));
          return lPicId;
        }

        ExifMetadata.Metadata exifData;

        // We need the date nevertheless for database view / sorting
        if (!GetExifDetails(strPicture, ref iRotation, ref strDateTaken, out exifData))
        {
          try
          {
            DateTime dat = File.GetLastWriteTime(strPicture);
            if (!TimeZone.CurrentTimeZone.IsDaylightSavingTime(dat))
            {
              dat = dat.AddHours(1); // Try to respect the timezone of the file date
            }
            strDateTaken = dat.ToString("yyyy-MM-dd HH:mm:ss");
          }
          catch (Exception ex)
          {
            Log.Error("Picture.DB.SQLite: Conversion exception getting file date: {0} stack:{1}", ex.Message, ex.StackTrace);
          }
        }

        // Save potential performance penalty
        if (_usePicasa)
        {
          if (GetPicasaRotation(strPic, ref iRotation))
          {
            Log.Debug("Picture.DB.SQLite: Changed rotation of image {0} based on picasa file to {1}", strPic, iRotation);
          }
        }

        // Transactions are a special case for SQLite - they speed things up quite a bit
        BeginTransaction();

        strSQL = String.Format("INSERT INTO picture (idPicture, strFile, iRotation, strDateTaken, iImageWidth, iImageHeight, iImageXReso, iImageYReso) VALUES " +
          "(NULL, '{0}',{1},'{2}','{3}','{4}','{5}','{6}')",
                                strPic, iRotation, strDateTaken, exifData.ImageDimensions.Width, exifData.ImageDimensions.Height, exifData.Resolution.Width, exifData.Resolution.Height);
        results = m_db.Execute(strSQL);
        if (results.Rows.Count > 0)
        {
          Log.Debug("Picture.DB.SQLite: Added Picture to database - {0}", strPic);
        }

        CommitTransaction();

        lPicId = m_db.LastInsertID();
        AddPictureExifData(lPicId, exifData);

        if (g_Player.Playing)
        {
          Thread.Sleep(50);
        }
        else
        {
          Thread.Sleep(1);
        }

        return lPicId;
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddPicture: {0} stack:{1}", ex.Message, ex.StackTrace);
        RollbackTransaction();
      }
      return -1;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public int UpdatePicture(string strPicture, int iRotation)
    {
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        return -1;
      }
      if (m_db == null)
      {
        return -1;
      }

      DeletePicture(strPicture);
      return AddPicture(strPicture, iRotation);
    }

    #region EXIF

    private string GetValueForQuery(int value)
    {
      return value < 0 ? "NULL" : value.ToString();
    }

    private string GetGPSValueForQuery(string value)
    {
      return (String.IsNullOrEmpty(value) || value.Equals("unknown", StringComparison.InvariantCultureIgnoreCase)) ? "NULL" : value.ToString();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    private void AddPictureExifData(int iDbID, ExifMetadata.Metadata exifData)
    {
      if (exifData.IsEmpty() || iDbID <= 0)
      {
        return;
      }

      try
      {
        BeginTransaction();

        AddKeywords(iDbID, exifData.Keywords.DisplayValue);

        try
        {
          string strSQL = String.Format("INSERT OR REPLACE INTO exiflinkpicture (idPicture, " +
                                                                         "idCamera, " +
                                                                         "idLens, " +
                                                                         "idOrientation, " +
                                                                         "idFlash, " +
                                                                         "idMeteringMode, " +
                                                                         "idExposureProgram, idExposureMode, " +
                                                                         "idSensingMethod, " +
                                                                         "idSceneType, idSceneCaptureType, " +
                                                                         "idWhiteBalance," +
                                                                         "idAuthor, idByline, " +
                                                                         "idSoftware, idUserComment, " +
                                                                         "idCopyright, idCopyrightNotice, " +
                                                                         "idCountry, idState, idCity, idSublocation, " +
                                                                         "idIso, idExposureTime, idExposureCompensation, idFstop, idShutterSpeed, idFocalLength, idFocalLength35mm, " +
                                                                         "strGPSLatitude, strGPSLongitude, strGPSAltitude) " +
                                   "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, " +
                                           "{12}, {13}, {14}, {15}, {16}, {17}, {18}, {19}, {20}, {21}, {22}, {23}, {24}, {25}, {26}, {27}, {28}, '{29}', '{30}', '{31}');",
                                            iDbID,
                                            GetValueForQuery(AddCamera(exifData.CameraModel.DisplayValue, exifData.EquipmentMake.DisplayValue)),
                                            GetValueForQuery(AddLens(exifData.Lens.DisplayValue, exifData.Lens.Value)),
                                            GetValueForQuery(AddOrienatation(exifData.Orientation.Value, exifData.Orientation.DisplayValue)),
                                            GetValueForQuery(AddFlash(exifData.Flash.Value, exifData.Flash.DisplayValue)),
                                            GetValueForQuery(AddMeteringMode(exifData.MeteringMode.Value, exifData.MeteringMode.DisplayValue)),
                                            GetValueForQuery(AddItem("ExposureProgram", exifData.ExposureProgram.DisplayValue)),
                                            GetValueForQuery(AddItem("ExposureMode", exifData.ExposureMode.DisplayValue)),
                                            GetValueForQuery(AddItem("SensingMethod", exifData.SensingMethod.DisplayValue)),
                                            GetValueForQuery(AddItem("SceneType", exifData.SceneType.DisplayValue)),
                                            GetValueForQuery(AddItem("SceneCaptureType", exifData.SceneCaptureType.DisplayValue)),
                                            GetValueForQuery(AddItem("WhiteBalance", exifData.WhiteBalance.DisplayValue)),
                                            GetValueForQuery(AddItem("Author", exifData.Author.DisplayValue)),
                                            GetValueForQuery(AddItem("Byline", exifData.ByLine.DisplayValue)),
                                            GetValueForQuery(AddItem("Software", exifData.ViewerComments.DisplayValue)),
                                            GetValueForQuery(AddItem("UserComment", exifData.Comment.DisplayValue)),
                                            GetValueForQuery(AddItem("Copyright", exifData.Copyright.DisplayValue)),
                                            GetValueForQuery(AddItem("CopyrightNotice", exifData.CopyrightNotice.DisplayValue)),
                                            GetValueForQuery(AddCountryo(exifData.CountryCode.DisplayValue, exifData.CountryName.DisplayValue)),
                                            GetValueForQuery(AddItem("State", exifData.ProvinceOrState.DisplayValue)),
                                            GetValueForQuery(AddItem("City", exifData.City.DisplayValue)),
                                            GetValueForQuery(AddItem("SubLocation", exifData.SubLocation.DisplayValue)),
                                            GetValueForQuery(AddItem("ISO", exifData.ISO.DisplayValue)),
                                            GetValueForQuery(AddItem("ExposureTime", exifData.ExposureTime.DisplayValue)),
                                            GetValueForQuery(AddItem("ExposureCompensation", exifData.ExposureCompensation.DisplayValue)),
                                            GetValueForQuery(AddItem("FStop", exifData.Fstop.DisplayValue)),
                                            GetValueForQuery(AddItem("ShutterSpeed", exifData.ShutterSpeed.DisplayValue)),
                                            GetValueForQuery(AddItem("FocalLength", exifData.FocalLength.DisplayValue)),
                                            GetValueForQuery(AddItem("FocalLength35mm", exifData.FocalLength35MM.DisplayValue)),
                                            GetGPSValueForQuery(DatabaseUtility.RemoveInvalidChars(exifData.Latitude.DisplayValue)),
                                            GetGPSValueForQuery(DatabaseUtility.RemoveInvalidChars(exifData.Longitude.DisplayValue)),
                                            GetGPSValueForQuery(DatabaseUtility.RemoveInvalidChars(exifData.Altitude.DisplayValue))
                                            );

          m_db.Execute(strSQL);
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: AddExifLinks: {0} stack:{1}", ex.Message, ex.StackTrace);
        }

        CommitTransaction();

      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddPictureExifData: {0} stack:{1}", ex.Message, ex.StackTrace);
        RollbackTransaction();
      }
    }

    private int AddItem(string tableName, string value)
    {
      if (value != null) value = Regex.Replace(value, @"[\u0000-\u001F]+", string.Empty);
      if (string.IsNullOrWhiteSpace(value))
      {
        return -1;
      }

      try
      {
        string strValue = DatabaseUtility.RemoveInvalidChars(value.Trim());
        string strSQL = String.Format("SELECT * FROM {0} WHERE str{0} = '{1}'", tableName, strValue);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO {0} (id{0}, str{0}) VALUES (NULL, '{1}')", tableName, strValue);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "id" + tableName), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: Add{0}: {1} stack:{2}", tableName, ex.Message, ex.StackTrace);
      }
      return -1;
    }



    private int AddCamera(string camera, string make)
    {
      if (string.IsNullOrWhiteSpace(camera))
      {
        return -1;
      }
      if (string.IsNullOrWhiteSpace(make))
      {
        make = string.Empty;
      }

      try
      {
        string strCamera = camera.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strCamera);
        string strMake = make.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strMake);

        string strSQL = String.Format("SELECT * FROM camera WHERE strCamera = '{0}'", strCamera);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO camera (idCamera, strCamera, strCameraMake) VALUES (NULL, '{0}', '{1}')", strCamera, strMake);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "idCamera"), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddCamera: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return -1;
    }

    private int AddLens(string lens, string make)
    {
      if (string.IsNullOrWhiteSpace(lens))
      {
        return -1;
      }
      if (null == m_db)
      {
        return -1;
      }
      if (string.IsNullOrWhiteSpace(make))
      {
        make = string.Empty;
      }

      try
      {
        string strLens = lens.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strLens);
        string strMake = make.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strMake);

        string strSQL = String.Format("SELECT * FROM lens WHERE strLens = '{0}'", strLens);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO lens (idLens, strLens, strLensMake) VALUES (NULL, '{0}', '{1}')", strLens, strMake);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "idLens"), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddLens: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return -1;
    }

    private int AddOrienatation(string id, string name)
    {
      if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
      {
        return -1;
      }
      if (null == m_db)
      {
        return -1;
      }

      try
      {
        string strId = id.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strId);
        string strName = name.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strName);

        string strSQL = String.Format("SELECT * FROM orientation WHERE idOrientation = '{0}'", strId);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO orientation (idOrientation, strOrientation) VALUES ('{0}', '{1}')", strId, strName);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "idOrientation"), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddOrienatation: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return -1;
    }

    private int AddFlash(string id, string name)
    {
      if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
      {
        return -1;
      }
      if (null == m_db)
      {
        return -1;
      }

      try
      {
        string strId = id.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strId);
        string strName = name.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strName);

        string strSQL = String.Format("SELECT * FROM flash WHERE idFlash = '{0}'", strId);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO flash (idFlash, strFlash) VALUES ('{0}', '{1}')", strId, strName);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "idFlash"), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddFlash: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return -1;
    }

    private int AddMeteringMode(string id, string name)
    {
      if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
      {
        return -1;
      }
      if (null == m_db)
      {
        return -1;
      }

      try
      {
        string strId = id.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strId);
        string strName = name.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strName);

        string strSQL = String.Format("SELECT * FROM meteringmode WHERE idMeteringMode = '{0}'", strId);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO meteringmode (idMeteringMode, strMeteringMode) VALUES ('{0}', '{1}')", strId, strName);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "idMeteringMode"), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddMeteringMode: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return -1;
    }



    private int AddCountry(string code, string name)
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        return -1;
      }
      if (null == m_db)
      {
        return -1;
      }
      if (string.IsNullOrWhiteSpace(code))
      {
        code = string.Empty;
      }

      try
      {
        string strCode = code.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strCode);
        string strName = name.Trim();
        DatabaseUtility.RemoveInvalidChars(ref strName);

        string strSQL = String.Format("SELECT * FROM country WHERE strCountry = '{0}'", strName);
        SQLiteResultSet results = m_db.Execute(strSQL);
        if (results.Rows.Count == 0)
        {
          strSQL = String.Format("INSERT INTO country (idCountry, strCountryCode, strCountry) VALUES (NULL, '{0}', '{1}')", strCode, strName);
          m_db.Execute(strSQL);
          int iID = m_db.LastInsertID();
          return iID;
        }
        else
        {
          int iID;
          if (Int32.TryParse(DatabaseUtility.Get(results, 0, "idCountry"), out iID))
          {
            return iID;
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddCountry: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return -1;
    }

    private void AddKeywords(int picID, string keywords)
    {
      if (string.IsNullOrWhiteSpace(keywords))
      {
        return;
      }
      if (null == m_db)
      {
        return;
      }

      try
      {
        string[] parts = keywords.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
          AddKeywordToPicture(AddItem("Keyword", part), picID);
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddKeywords: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
    }

    private void AddKeywordToPicture(int keyID, int picID)
    {
      if (keyID <= 0 || picID <= 0)
      {
        return;
      }
      if (null == m_db)
      {
        return;
      }

      try
      {
        string strSQL = String.Format("INSERT INTO keywordslinkpicture (idKeyword, idPicture) VALUES ('{0}', '{1}')", keyID, picID);
        m_db.Execute(strSQL);
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: AddKeywordToPicture: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
    }

    public ExifMetadata.Metadata GetExifData(string strPicture)
    {

      if (!Util.Utils.IsPicture(strPicture))
      {
        return new ExifMetadata.Metadata();
      }

      using (ExifMetadata extractor = new ExifMetadata())
      {
        return extractor.GetExifMetadata(strPicture);
      }
    }

    private string GetExifDBKeywords(int idPicture)
    {
      if (idPicture < 1)
      {
        return string.Empty;
      }
      if (m_db == null)
      {
        return string.Empty;
      }

      try
      {
        string SQL = String.Format("SELECT strKeyword FROM picturekeywords WHERE idPicture = {0} ORDER BY 1", idPicture);
        SQLiteResultSet results = m_db.Execute(SQL);
        if (results != null && results.Rows.Count > 0)
        {
          StringBuilder result = new StringBuilder();
          for (int i = 0; i < results.Rows.Count; i++)
          {
            string keyw = results.Rows[i].fields[0].Trim();
            if (!String.IsNullOrEmpty(keyw))
            {
              if (result.Length > 0)
                result.Append("; ");
              result.Append(keyw);
            }
          }
          return result.ToString();
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: GetExifDBKeywords: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return string.Empty;
    }

    private bool AssignAllExifFieldsFromResultSet(ref ExifMetadata.Metadata aExif, SQLiteResultSet aResult, int aRow)
    {
      if (aResult == null || aResult.Rows.Count < 1)
      {
        return false;
      }

      aExif.DatePictureTaken.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strDateTaken");
      aExif.Orientation.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strOrientation");
      aExif.EquipmentMake.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCameraMake");
      aExif.CameraModel.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCamera");
      aExif.Lens.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strLens");
      aExif.Fstop.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strFStop");
      aExif.ShutterSpeed.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strShutterSpeed");
      aExif.ExposureTime.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strExposureTime");
      aExif.ExposureCompensation.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strExposureCompensation");
      aExif.ExposureProgram.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strExposureProgram");
      aExif.ExposureMode.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strExposureMode");
      aExif.MeteringMode.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strMeteringMode");
      aExif.Flash.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strFlash");
      aExif.ISO.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strISO");
      aExif.WhiteBalance.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strWhiteBalance");
      aExif.SensingMethod.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strSensingMethod");
      aExif.SceneType.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strSceneType");
      aExif.SceneCaptureType.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strSceneCaptureType");
      aExif.FocalLength.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strFocalLength");
      aExif.FocalLength35MM.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strFocalLength35mm");
      aExif.CountryCode.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCountryCode");
      aExif.CountryName.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCountry");
      aExif.ProvinceOrState.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strState");
      aExif.City.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCity");
      aExif.SubLocation.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strSublocation");
      aExif.Author.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strAuthor");
      aExif.Copyright.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCopyright");
      aExif.CopyrightNotice.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strCopyrightNotice");
      aExif.Comment.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strUserComment");
      aExif.ViewerComments.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strSoftware");
      aExif.ByLine.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strByline");
      aExif.Latitude.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strGPSLatitude");
      aExif.Longitude.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strGPSLongitude");
      aExif.Altitude.DisplayValue = DatabaseUtility.Get(aResult, aRow, "strGPSAltitude");
      aExif.ImageDimensions.Width = DatabaseUtility.GetAsInt(aResult, aRow, "iImageWidth");
      aExif.ImageDimensions.Height = DatabaseUtility.GetAsInt(aResult, aRow, "iImageHeight");
      aExif.Resolution.Width = DatabaseUtility.GetAsInt(aResult, aRow, "iImageXReso");
      aExif.Resolution.Height = DatabaseUtility.GetAsInt(aResult, aRow, "iImageYReso");

      try
      {
        aExif.Orientation.Value = DatabaseUtility.GetAsInt(aResult, aRow, "idOrientation").ToString();
        aExif.MeteringMode.Value = DatabaseUtility.GetAsInt(aResult, aRow, "idMeteringMode").ToString();
        aExif.Flash.Value = DatabaseUtility.GetAsInt(aResult, aRow, "idFlash").ToString();
      }
      catch (Exception ex)
      {
        Log.Warn("Picture.DB.SQLite: Exception parsing integer fields: {0} stack:{1}", ex.Message, ex.StackTrace);
      }

      try
      {
        aExif.DatePictureTaken.Value = DatabaseUtility.GetAsDateTime(aResult, aRow, "strDateTaken").ToString();
        /*
        DateTimeFormatInfo dateTimeFormat = new DateTimeFormatInfo();
        dateTimeFormat.ShortDatePattern = "yyyy-MM-dd HH:mm:ss";
        aExif.DatePictureTaken.Value = DateTime.ParseExact(aExif.DatePictureTaken.DisplayValue, "d", dateTimeFormat).ToString();
        */
      }
      catch (Exception ex)
      {
        Log.Warn("Picture.DB.SQLite: Exception parsing date fields: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return true;
    }

    public ExifMetadata.Metadata GetExifDBData(string strPicture)
    {
      if (m_db == null || !Util.Utils.IsPicture(strPicture))
      {
        return new ExifMetadata.Metadata();
      }

      ExifMetadata.Metadata metaData = new ExifMetadata.Metadata();
      try
      {
        string strPic = strPicture;
        DatabaseUtility.RemoveInvalidChars(ref strPic);

        string SQL = String.Format("SELECT idPicture FROM picture WHERE strFile LIKE '{0}'", strPic);
        SQLiteResultSet results = m_db.Execute(SQL);
        if (results != null && results.Rows.Count > 0)
        {
          int idPicture = DatabaseUtility.GetAsInt(results, 0, "idPicture");
          if (idPicture > 0)
          {
            SQL = String.Format("SELECT * FROM picturedata WHERE idPicture = {0}", idPicture);
            results = m_db.Execute(SQL);
            if (results != null && results.Rows.Count > 0)
            {
              AssignAllExifFieldsFromResultSet(ref metaData, results, 0);
              metaData.Keywords.DisplayValue = GetExifDBKeywords(idPicture);
            }
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: GetExifDBData: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return metaData;
    }

    private bool GetExifDetails(string strPicture, ref int iRotation, ref string strDateTaken, out ExifMetadata.Metadata metaData)
    {
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        metaData = new ExifMetadata.Metadata();
        return false;
      }

      metaData = GetExifData(strPicture);
      if (metaData.IsEmpty())
      {
        return false;
      }

      try
      {
        strDateTaken = metaData.DatePictureTaken.Value;
        if (!string.IsNullOrWhiteSpace(strDateTaken))
        // If the image contains a valid exif date store it in the database, otherwise use the file date
        {
          if (_useExif)
          {
            iRotation = EXIFOrientationToRotation(Convert.ToInt32(metaData.Orientation.Value));
          }
          return true;
        }
      }
      catch (FormatException ex)
      {
        Log.Error("Picture.DB.SQLite: Exif details: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      strDateTaken = string.Empty;
      return false;
    }

    public int EXIFOrientationToRotation(int orientation)
    {
      return orientation.ToRotation();
    }

    #endregion

    private bool GetPicasaRotation(string strPic, ref int iRotation)
    {
      bool foundValue = false;
      if (File.Exists(Path.GetDirectoryName(strPic) + "\\Picasa.ini"))
      {
        using (StreamReader sr = File.OpenText(Path.GetDirectoryName(strPic) + "\\Picasa.ini"))
        {
          try
          {
            string s = string.Empty;
            bool searching = true;
            while ((s = sr.ReadLine()) != null && searching)
            {
              if (s.ToLowerInvariant() == "[" + Path.GetFileName(strPic).ToLowerInvariant() + "]")
              {
                do
                {
                  s = sr.ReadLine();
                  if (s.StartsWith("rotate=rotate("))
                  {
                    // Find out Rotate Setting
                    try
                    {
                      iRotation = int.Parse(s.Substring(14, 1));
                      foundValue = true;
                    }
                    catch (Exception ex)
                    {
                      Log.Error("Picture.DB.SQLite: Error converting number picasa.ini", ex.Message, ex.StackTrace);
                    }
                    searching = false;
                  }
                } while (s != null && !s.StartsWith("[") && searching);
              }
            }
          }
          catch (Exception ex)
          {
            Log.Error("Picture.DB.SQLite: File read problem picasa.ini", ex.Message, ex.StackTrace);
          }
        }
      }
      return foundValue;
    }

    public string GetDateTaken(string strPicture)
    {
      if (m_db == null)
      {
        return string.Empty;
      }
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        return string.Empty;
      }

      string result = string.Empty;

      try
      {
        string strPic = strPicture;
        DatabaseUtility.RemoveInvalidChars(ref strPic);

        string SQL = String.Format("SELECT strDateTaken FROM picture WHERE strFile LIKE '{0}'", strPic);
        SQLiteResultSet results = m_db.Execute(SQL);
        if (results != null && results.Rows.Count > 0)
        {
          result = DatabaseUtility.Get(results, 0, "strDateTaken");
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: GetDateTaken: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return result;
    }

    public DateTime GetDateTimeTaken(string strPicture)
    {
      string dbDateTime = GetDateTaken(strPicture);
      if (string.IsNullOrEmpty(dbDateTime))
      {
        return DateTime.MinValue;
      }

      try
      {
        DateTimeFormatInfo dateTimeFormat = new DateTimeFormatInfo();
        dateTimeFormat.ShortDatePattern = "yyyy-MM-dd HH:mm:ss";
        return DateTime.ParseExact(dbDateTime, "d", dateTimeFormat);
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: GetDateTaken Date parse Error: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return DateTime.MinValue;
    }

    public int GetRotation(string strPicture)
    {
      if (m_db == null)
      {
        return -1;
      }
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        return -1;
      }

      try
      {
        string strPic = strPicture;
        int iRotation = 0;
        DatabaseUtility.RemoveInvalidChars(ref strPic);

        SQLiteResultSet results = m_db.Execute(String.Format("SELECT strFile, iRotation FROM picture WHERE strFile LIKE '{0}'", strPic));
        if (results != null && results.Rows.Count > 0)
        {
          iRotation = Int32.Parse(DatabaseUtility.Get(results, 0, 1));
          return iRotation;
        }

        if (_useExif)
        {
          iRotation = Util.Picture.GetRotateByExif(strPicture);
          Log.Debug("Picture.DB.SQLite: GetRotateByExif = {0} for {1}", iRotation, strPicture);
        }

        AddPicture(strPicture, iRotation);

        return iRotation;
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: GetRotation: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
      return 0;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SetRotation(string strPicture, int iRotation)
    {
      if (m_db == null)
      {
        return;
      }
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        return;
      }

      try
      {
        string strPic = strPicture;
        DatabaseUtility.RemoveInvalidChars(ref strPic);

        long lPicId = AddPicture(strPicture, iRotation);
        if (lPicId >= 0)
        {
          m_db.Execute(String.Format("UPDATE picture SET iRotation={0} WHERE strFile LIKE '{1}'", iRotation, strPic));
        }
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: {0} stack:{1}", ex.Message, ex.StackTrace);
      }
    }

    public void DeletePicture(string strPicture)
    {
      // Continue only if it's a picture files
      if (!Util.Utils.IsPicture(strPicture))
      {
        return;
      }
      if (m_db == null)
      {
        return;
      }

      lock (typeof(PictureDatabase))
      {
        try
        {
          string strPic = strPicture;
          DatabaseUtility.RemoveInvalidChars(ref strPic);

          string strSQL = String.Format("DELETE FROM picture WHERE strFile LIKE '{0}'", strPic);
          m_db.Execute(strSQL);
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Deleting picture err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return;
      }
    }

    private string GetSearchQuery(string find)
    {
      string result = string.Empty;
      MatchCollection matches = Regex.Matches(find, @"([+|]?[^+|;]+;?)");
      foreach (Match match in matches)
      {
        foreach (Capture capture in match.Captures)
        {
          if (!string.IsNullOrEmpty(capture.Value))
          {
            string part = capture.Value;
            if (part.Contains("+"))
            {
              part = part.Replace("+", " AND {0} = '") + "'";
            }
            if (part.Contains("|"))
            {
              part = part.Replace("|", " OR {0} = '") + "'";
            }
            if (part.Contains(";"))
            {
              part = "{0} = '" + part.Replace(";", "' AND ");
            }
            if (!part.Contains("{0}"))
            {
              part = "{0} = '" + part + "'";
            }
            if (part.Contains("%"))
            {
              part = part.Replace("{0} = '", "{0} LIKE '");
            }
            result = result + part;
          }
        }
      }
      Log.Debug("Picture.DB.SQLite: Search -> Where: {0} -> {1}", find, result);
      // Picture.DB.SQLite: Search -> Where: word;word1+word2|word3|%like% -> {0} = 'word' AND {0} = 'word1' AND {0} = 'word2' OR {0} = 'word3' OR {0} LIKE '%like%'
      // Picture.DB.SQLite: Search -> Where: word -> {0} = 'word'
      // Picture.DB.SQLite: Search -> Where: word%|word2 -> {0} LIKE 'word%' OR {0} = 'word2'
      return result;
    }
    /*
        private string GetSearchQuery (string find)
        {
          string result = string.Empty;
          MatchCollection matches = Regex.Matches(find, @"([|]?[^|]+)");
          foreach (Match match in matches)
          {
            foreach (Capture capture in match.Captures)
            {
              if (!string.IsNullOrEmpty(capture.Value))
              {
                string part = capture.Value;
                if (part.Contains("+") || part.Contains(";"))
                {
                  part = part.Replace("+", ",");
                  part = part.Replace(";", ",");
                  part = part.Replace(",", "','");
                }
                if (part.Contains("|"))
                {
                  part = part.Replace("|", " OR {0} = '") + "'";
                }
                if (!part.Contains("{0}"))
                {
                  part = "{0} = '" + part + "'";
                }
                if (part.Contains(","))
                {
                  part = part.Replace("{0} = ", " {0} IN (") + ")";
                }
                if (part.Contains("%"))
                {
                  part = part.Replace("{0} = '", "{0} LIKE '");
                }
                result = result + part;
              }
            }
          }
          Log.Debug ("Picture.DB.SQLite: Search -> Where: {0} -> {1}", find, result);
          // Picture.DB.SQLite: Search -> Where: word;word1+word2|word3|%like% ->  {0} IN ('word','word1','word2') OR {0} = 'word3' OR {0} LIKE '%like%'
          // Picture.DB.SQLite: Search -> Where: word -> {0} = 'word'
          // Picture.DB.SQLite: Search -> Where: word%|word2 -> {0} LIKE 'word%' OR {0} = 'word2'
          return result;
        }
    */
    private string GetSearchWhere(string keyword, string where)
    {
      if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(where))
      {
        return string.Empty;
      }
      return "WHERE " + string.Format(GetSearchQuery(where), keyword);
    }

    private string GetSelect(string field, string search)
    {
      if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(field))
      {
        return string.Empty;
      }

      if (!search.Contains("Private"))
      {
        search += "#!Private";
      }

      string result = string.Empty;
      string[] lines = search.Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries);
      if (lines.Length == 0)
      {
        return string.Empty;
      }

      string firstPart = string.Format("SELECT DISTINCT {0} FROM picturekeywords {1}", field, GetSearchWhere("strKeyword", lines[0]));
      string debug = string.Empty;
      for (int i = 1; i < lines.Length; i++)
      {
        debug = debug + (string.IsNullOrEmpty(debug) ? string.Empty : " <- ") + lines[i];
        string sql = string.Format("SELECT DISTINCT idPicture FROM picturekeywords {0}", GetSearchWhere("strKeyword", lines[i].Replace("!", "")));
        result += string.Format(string.IsNullOrEmpty(result) ? "{0}" : " AND idPicture {1}IN ({0}", sql, lines[i].Contains("!") ? "NOT " : string.Empty);
      }
      if (!string.IsNullOrEmpty(result))
      {
        debug = lines[0] + (string.IsNullOrEmpty(debug) ? string.Empty : " <- ") + debug;
        result = result + new String(')', lines.Length - 2);
        result = string.Format("{0} AND idPicture {2}IN ({1})", firstPart, result, lines.Length >= 2 && lines[1].Contains("!") ? "NOT " : string.Empty);
      }
      else
      {
        result = firstPart;
      }

      if (lines.Length > 1)
      {
        Log.Debug("Multi search: " + debug);
      }
      return result + " ORDER BY strDateTaken";
      // GetSelect("qwe") -> SELECT DISTINCT strFile FROM picturekeywords WHERE strKeyword = 'qwe' ORDER BY strDateTaken
      // GetSelect("qwe#aaa") -> SELECT DISTINCT strFile FROM picturekeywords WHERE strKeyword = 'qwe' AND idPicture IN (SELECT DISTINCT idPicture FROM picturekeywords WHERE strKeyword = 'aaa') ORDER BY strDateTaken
      // GetSelect("q1#q2#q3.1|q3.2#q4%#q5") -> SELECT DISTINCT strFile FROM picturekeywords WHERE strKeyword = 'q1' AND idPicture IN (SELECT DISTINCT idPicture FROM picturekeywords WHERE strKeyword = 'q2' AND idPicture IN (SELECT DISTINCT idPicture FROM picturekeywords WHERE strKeyword = 'q3.1' OR strKeyword = 'q3.2' AND idPicture IN (SELECT DISTINCT idPicture FROM picturekeywords WHERE strKeyword LIKE 'q4%' AND idPicture IN (SELECT DISTINCT idPicture FROM picturekeywords WHERE strKeyword = 'q5')))) ORDER BY strDateTaken
    }

    public int ListKeywords(ref List<string> Keywords)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT DISTINCT strKeyword FROM keywords WHERE strKeyword <> 'Private' ORDER BY 1";
        try
        {
          SQLiteResultSet result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Keywords.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Keywords err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int ListPicsByKeyword(string Keyword, ref List<string> Pics)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT strFile FROM picturekeywords WHERE strKeyword = '" + Keyword + "' " +
                               "AND idPicture NOT IN (SELECT idPicture FROM picturekeywords WHERE strKeyword = 'Private') " +
                               "ORDER BY strDateTaken";
        try
        {
          SQLiteResultSet result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Pics.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Picture by Keyword err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int CountPicsByKeyword(string Keyword)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT COUNT(strFile) FROM picturekeywords WHERE strKeyword = '" + Keyword + "' " +
                               "AND idPicture NOT IN (SELECT idPicture FROM picturekeywords WHERE strKeyword = 'Private') " +
                               "ORDER BY strDateTaken";
        try
        {
          SQLiteResultSet result = m_db.Execute(strSQL);
          if (result != null)
          {
            Count = DatabaseUtility.GetAsInt(result, 0, 0);
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Count of Picture by Keyword err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int ListPicsByKeywordSearch(string Keyword, ref List<string> Pics)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = GetSelect("strFile", Keyword);
        try
        {
          SQLiteResultSet result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Pics.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Picture by Keyword Search err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int CountPicsByKeywordSearch(string Keyword)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = GetSelect("COUNT(strFile)", Keyword);
        try
        {
          SQLiteResultSet result = m_db.Execute(strSQL);
          if (result != null)
          {
            Count = DatabaseUtility.GetAsInt(result, 0, 0);
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Count of Picture by Keyword Search err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int ListYears(ref List<string> Years)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT DISTINCT SUBSTR(strDateTaken,1,4) FROM picture ORDER BY 1";
        SQLiteResultSet result;
        try
        {
          result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Years.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Years err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int ListMonths(string Year, ref List<string> Months)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT DISTINCT SUBSTR(strDateTaken,6,2) FROM picture WHERE strDateTaken LIKE '" + Year + "%' ORDER BY strDateTaken";
        SQLiteResultSet result;
        try
        {
          result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Months.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Months err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int ListDays(string Month, string Year, ref List<string> Days)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT DISTINCT SUBSTR(strDateTaken,9,2) FROM picture WHERE strDateTaken LIKE '" + Year + "-" + Month + "%' ORDER BY strDateTaken";
        SQLiteResultSet result;
        try
        {
          result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Days.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Days err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int ListPicsByDate(string Date, ref List<string> Pics)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT strFile FROM picture WHERE strDateTaken LIKE '" + Date + "%' " +
                        "AND idPicture NOT IN (SELECT idPicture FROM picturekeywords WHERE strKeyword = 'Private')" +
                        "ORDER BY strDateTaken";
        SQLiteResultSet result;
        try
        {
          result = m_db.Execute(strSQL);
          if (result != null)
          {
            for (Count = 0; Count < result.Rows.Count; Count++)
            {
              Pics.Add(DatabaseUtility.Get(result, Count, 0));
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Picture by Date err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    public int CountPicsByDate(string Date)
    {
      if (m_db == null)
      {
        return 0;
      }

      int Count = 0;
      lock (typeof(PictureDatabase))
      {
        string strSQL = "SELECT COUNT(strFile) FROM picture WHERE strDateTaken LIKE '" + Date + "%' " +
                        "AND idPicture NOT IN (SELECT idPicture FROM picturekeywords WHERE strKeyword = 'Private')" +
                        "ORDER BY strDateTaken";
        SQLiteResultSet result;
        try
        {
          result = m_db.Execute(strSQL);
          if (result != null)
          {
            Count = DatabaseUtility.GetAsInt(result, 0, 0);
          }
        }
        catch (Exception ex)
        {
          Log.Error("Picture.DB.SQLite: Getting Count Picture by Date err: {0} stack:{1}", ex.Message, ex.StackTrace);
        }
        return Count;
      }
    }

    #region Transactions

    private void BeginTransaction()
    {
      if (m_db == null)
      {
        return;
      }

      try
      {
        m_db.Execute("BEGIN");
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: Begin transaction failed exception err: {0} ", ex.Message);
        Open();
      }
    }

    private void CommitTransaction()
    {
      if (m_db == null)
      {
        return;
      }

      try
      {
        m_db.Execute("COMMIT");
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: Commit failed exception err: {0} ", ex.Message);
        RollbackTransaction();
      }
    }

    private void RollbackTransaction()
    {
      if (m_db == null)
      {
        return;
      }

      try
      {
        m_db.Execute("ROLLBACK");
      }
      catch (Exception ex)
      {
        Log.Error("Picture.DB.SQLite: Rollback failed exception err: {0} ", ex.Message);
        Open();
      }
    }

    #endregion

    public bool DbHealth
    {
      get
      {
        return _dbHealth;
      }
    }

    public string DatabaseName
    {
      get
      {
        if (m_db != null)
        {
          return m_db.DatabaseName;
        }
        return string.Empty;
      }
    }

    #region IDisposable Members

    public void Dispose()
    {
      if (!disposed)
      {
        disposed = true;
        if (m_db != null)
        {
          try
          {
            m_db.Close();
            m_db.Dispose();
          }
          catch (Exception ex)
          {
            Log.Error("Picture.DB.SQLite: Dispose: {0}", ex.Message);
          }
          m_db = null;
        }
      }
    }

    #endregion
  }
}