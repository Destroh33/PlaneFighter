using UnityEngine;
using Unity.Netcode;

public class NetworkUI : MonoBehaviour
{
    public void Host()
    {
        Debug.Log("Host button clicked");
        NetworkManager.Singleton.StartHost();
    }
    public void Client() => NetworkManager.Singleton.StartClient();
}