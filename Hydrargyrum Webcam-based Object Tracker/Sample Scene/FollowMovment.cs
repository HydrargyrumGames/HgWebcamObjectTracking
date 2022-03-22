using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowMovment : MonoBehaviour
{
    public GameObject Cursor;
    HgTracker HgTracker;
    HgTracker.TrackPod TP;
    bool TrackerAvalible = true;
    Camera cam;

    // Start is called before the first frame update
    void Start()
    {
        HgTracker = GameObject.Find("HgTracker").GetComponent<HgTracker>();
        if (HgTracker.Trackers.Length > 0)
        {
            TP = HgTracker.Trackers[0];
            HgTracker.Trackers[0].ScreenSpacePositions = true;
            TrackerAvalible = true;
        }
        cam = Camera.main;
    }

    // Update is called once per frame
    void Update()
    {
        TP = HgTracker.Trackers[0];
        if (TrackerAvalible && TP.Is_Visible) 
        {
            Vector2 Position = TP.SmoothedPosition;
            if (TP.ScreenSpacePositions)
            {
                Position -= new Vector2(.5f, .5f);
                Position.x *= 3.55f;
                Position.y *= 2f;
                Position *= cam.orthographicSize;

                Cursor.transform.position = Position;
            }
        }
    }
}
