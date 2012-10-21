using MediaPortal.Common.Utils;
using MediaPortal.GUI.Library;
using Mediaportal.TV.Server.TVLibrary.Interfaces.CiMenu;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Interfaces;

namespace Mediaportal.TV.TvPlugin.EventHandlers
{
  /// <summary>
  /// Handler class for gui interactions of ci menu
  /// </summary>
  public class CiMenuEventEventHandler : CiMenuEventCallbackSink
  {
    #region logging

    private static ILogManager Log
    {
      get { return LogHelper.GetLogger(typeof(CiMenuEventEventHandler)); }
    }

    #endregion

    /// <summary>
    /// eventhandler to show CI Menu dialog
    /// </summary>
    /// <param name="menu"></param>
    public override void CiMenuCallback(CiMenu menu)
    {
      try
      {
        Log.DebugFormat("Callback from tvserver {0}", menu.Title);

        // pass menu to calling dialog
        TvPlugin.TVHome.ProcessCiMenu(menu);
      }
      catch
      {
        menu = new CiMenu("Remoting Exception", "Communication with server failed", null,
                          CiMenuState.Error);
        // pass menu to calling dialog
        TvPlugin.TVHome.ProcessCiMenu(menu);
      }
    }
  }
}