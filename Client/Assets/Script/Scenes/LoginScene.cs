using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LoginScene : BaseScene
{
    UI_LoginScene _sceneUI;
    protected override void Init()
    {
        base.Init();

        SceneType = Define.Scene.Login;

        Managers.Web.BaseUrl = "http://localhost:5000/api";

        Screen.SetResolution(640, 480, false);

        _sceneUI = Managers.UI.ShowSceneUI<UI_LoginScene>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            GameObject current = EventSystem.current.currentSelectedGameObject;
            if (current == null) return;

            Selectable selectable = current.GetComponent<Selectable>();
            if (selectable != null)
            {
                Selectable next = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                    ? selectable.FindSelectableOnUp()
                    : selectable.FindSelectableOnDown();

                if (next != null)
                {
                    next.Select();
                }
            }
        }
    }

    public override void Clear()
    {

    }
}
