// The HeadsetPositionFollowerByCharacter(), CapsuleFollowHeadSet(), and CheckIfGrounded() are inspired by the tutorials of "Valem" [https://www.youtube.com/watch?v=5NRTT8Tbmoc]. However, the functions have been tweaked to make them work with the current version of XR Interaction Toolkit, the New Unity Input System, and to support the current locomotion method.

// Procedure to move:-
// The "Trigger Button" of your dominant hand controller would allow you to move forward in the virtual environment.
// You need to hold the non-dominant hand controller on the side of your body, as this controller would act as your body tracker.
// You need to physically rotate towards your intended movement direction to move towards a particular direction in the virtual scene.
// If you are deflected in the scene because of your non-dominant hand movement, you can recalibrate it by -
// Resting your non-dominant hand once again on your side first.
// Then press the "Primary Button" of the non-dominant hand controller to recalibrate the movement direction.
// Once the recalibration is done, you will be able to move in your intended direction once again!!!

using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(CharacterController))]
public class Modified_Steering_Controller : MonoBehaviour
{
    private int Which_Handed_Person;
    private CharacterController character;
    public float speed = 1; // A constant speed of 1 m/s is used in our study.
    private float gravity = -9.81f;
    private float fallingSpeed;
    public LayerMask groundLayer; // You can create a Physics Layer called 'Ground' and assign the layer to the objects where the user can steer. Then choose 'Ground' from the dropdown menu of this variable on the Unity Editor. Otherwise, you can simply choose 'Everything' from the dropdown menu of this variable in the inspector! This layer is to allow sphere casting in every frame by the application to check if the user is on the ground or not. 
    private float additionalHeight = 0.2f;
    private float inputButtonValue;
    private bool calibration_Invoked, initialSetupDone, UI_Interaction_Completed;
    private Quaternion Yaw = Quaternion.Euler(Vector3.zero);
    private float previous_y_value = 0, yRotHand = 0, value = 0;
    private Vector3 direction, groundPos, sphereCastHitPoint;
    public GameObject MainCamera, LeftController, RightController, Canvas, EventSystem, GroundContact; // Assign the 'Main Camera', 'Left Controller', and 'Right Controller' Game Objects under the XROrigin Game Object, to the respective Game Objects of these script. Create a UI Text Mesh Pro Text Game Object, and two UI Text Mesh Pro Buttons in the scene. The Text field can ask users - "You are a...". One of the button will show - "Right-Handed Person", and the other one will show - "Left-Handed Person". Assign the "RightHandActivated()" and "LeftHandActivated()" functions to the OnClick() event of the respective buttons to allow the application know the dominant and non-dominant hand controllers of the user. These UI elements will generate a 'Canvas' and an 'EventSystem' Game Objects. Assign those two Game Objects to the respective Game Objects of this script. 
    public InputActionProperty leftActivateAction, rightActivateAction, leftPrimaryButton, rightPrimaryButton; // Assign the "Activate" (trigger buttons) and "Primary Button" Input actions (A or X buttons) of the Left and Right Hand Controllers to the respective InputActionProperty variables of this script.
    private XROrigin xROrigin;

    // Start is called before the first frame update
    void Start()
    {
        character = GetComponent<CharacterController>();
        character.radius = 0.15f;
        character.height = 2.0f;
        xROrigin = this.gameObject.GetComponent<XROrigin>();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (!initialSetupDone) // This checking is to make sure that the XROrigin and the Main Camera's initial direction matches up.
        {
            if (MainCamera.transform.position != Vector3.zero && MainCamera.transform.rotation != Quaternion.identity)
            {
                Quaternion rot1 = MainCamera.transform.rotation;
                Quaternion rot2 = MainCamera.transform.parent.parent.rotation;
                Vector3 rotA = rot1.eulerAngles;
                rotA.x = 0;
                rotA.z = 0;
                rot1 = Quaternion.Euler(rotA);
                float Angle = Quaternion.Angle(rot1, rot2);
                MainCamera.transform.parent.parent.Rotate(new Vector3(0, Angle, 0));
                Vector3 camPos = MainCamera.transform.position;
                camPos.y = MainCamera.transform.parent.parent.position.y;
                Vector3 dir = (Vector3.zero - camPos).normalized;
                float dist = Vector3.Distance(Vector3.zero, camPos);
                MainCamera.transform.parent.parent.position += dir * dist;
                initialSetupDone = true;
                Main_Calibrator();
            }
        }
        else
        {
            if (UI_Interaction_Completed) // The Steering starts after interacting with the UI
            {
                RecalibrationActivator();
                Main_Steering();
                CharacterMover();
                HeadsetPositionFollowerByCharacter();
            }
        }
    }

    public void RightHandActivated() // This function is attached to the 'Right-handed Person' UI button in the scene.
    {
        Which_Handed_Person = 1;
        PostButtonClicked(); // Turns off the laser pointers of the controllers. Also, it will destroy the Canvas and the EventSystem so that they will not be visible in the scene any more.
        Main_Steering(); // Gets the current global Y Rotation angle of the non-dominant Hand Controller.
        Main_Calibrator(); // We assumes that the user has already placed their non-dominant hand at a comfortable place (most likely on their sides close to their waists). So, the initial calibration will be performed to calculate the heading direction of the user.
        UI_Interaction_Completed = true; // This variable lets the application know that the steering calculation will be starting from the next frame.
    }

    public void LeftHandActivated() // This function is attached to the 'Left-handed Person' UI button in the scene.
    {
        Which_Handed_Person = 2;
        PostButtonClicked();
        Main_Steering();
        Main_Calibrator();
        UI_Interaction_Completed = true;
    }

    private void PostButtonClicked() // This function turns off the laser rays emanating from the controllers after selecting the UI buttons in the scene.
    {
        LeftController.gameObject.GetComponent<XRRayInteractor>().enabled = false;
        LeftController.gameObject.GetComponent<LineRenderer>().enabled = false;
        LeftController.gameObject.GetComponent<XRInteractorLineVisual>().enabled = false;
        RightController.gameObject.GetComponent<XRRayInteractor>().enabled = false;
        RightController.gameObject.GetComponent<LineRenderer>().enabled = false;
        RightController.gameObject.GetComponent<XRInteractorLineVisual>().enabled = false;
        Destroy(Canvas);
        Destroy(EventSystem);
    }

    private void Main_Steering() // This function collects the current global Y rotation angle of the non-dominant hand controller, and how much the trigger button is pressed in the current frame.
    {
        if (Which_Handed_Person == 1) // If this variable value is 1, then the person is a right-handed person, or else he/she is a left-handed person. In the first case, the body tracker would be left-hand tracker, and for the second case, it would be the right hand tracker.
        {
            inputButtonValue = rightActivateAction.action.ReadValue<float>(); // This variable determines how much the trigger button is pressed. If the value is greater than 0, it means the trigger button is pressed by some amount. This triggers the continuous steering movement.
            yRotHand = LeftController.transform.eulerAngles.y; // This is the non-dominant hand controller's global Y rotation angle.
        }
        else if (Which_Handed_Person == 2)
        {
            inputButtonValue = leftActivateAction.action.ReadValue<float>();
            yRotHand = RightController.transform.eulerAngles.y;
        }
    }

    private void Main_Calibrator() // This function calls the recalibration mechanism whenever a new trial is started or whenever the participants performs the recalibration intentionally.
    {
        GameObject xa = new GameObject();
        Vector3 pos = MainCamera.transform.position;
        pos.y = 0;
        Vector3 rot = MainCamera.transform.rotation.eulerAngles;
        rot.x = 0;
        rot.z = 0;
        xa.transform.position = pos;
        xa.transform.rotation = Quaternion.Euler(rot); // This new Game Object gets the global x and z positions and global y rotation angle of the Main Camera. 
        Vector3 rot1 = xa.transform.forward;
        Vector3 rot2 = this.transform.forward; // Here 'this' means the XROrigin because the script is attached to this Game Object. We need to move the entire XRORigin while steering. So, we considered this Game Object. 
        float angularDifference = Vector3.Angle(rot2, rot1);
        Vector3 cross = Vector3.Cross(rot2, rot1);

        if (cross.y < 0)
            angularDifference = -angularDifference;

        Yaw = Quaternion.Euler(new Vector3(0, angularDifference, 0));
        direction = Yaw * this.transform.forward;
        calibration_Invoked = true;
        previous_y_value = yRotHand;
        Destroy(xa);
    }

    private void RecalibrationActivator() // This function checks and activates the recalibration function if the primary button of the non-dominant hand controller is pressed.
    {
        bool isPressed = false;

        if (Which_Handed_Person == 1)
            isPressed = leftPrimaryButton.action.IsPressed();
        else if (Which_Handed_Person == 2)
            isPressed = rightPrimaryButton.action.IsPressed();

        if (isPressed)
            Main_Calibrator();
    }

    private void CharacterMover() // This function moves the user in the calibrated heading direction.
    {
        if (inputButtonValue > 0)
        {
            if (calibration_Invoked == true)
            {
                character.Move(direction * Time.fixedDeltaTime * speed);
                calibration_Invoked = false;
            }
            else
            {
                value = yRotHand - previous_y_value;
                Yaw = Quaternion.Euler(new Vector3(0, value, 0));
                Vector3 dir = Yaw * direction;
                character.Move(dir * Time.fixedDeltaTime * speed);
            }
        }
    }

    private void HeadsetPositionFollowerByCharacter() // This function and the other functions mentioned inside it allow the CharacterController to follow the headset's position in the scene.
    {
        CapsuleFollowHeadSet();
        //gravity
        bool isGrounded = CheckIfGrounded();

        if (isGrounded)
        {
            float dist = Vector3.Distance(groundPos, sphereCastHitPoint);
            character.Move(Vector3.down * dist);
        }
        else
        {
            fallingSpeed += gravity * Time.fixedDeltaTime;   
            character.Move(Vector3.up * fallingSpeed * Time.fixedDeltaTime);    
        }
    }

    private void CapsuleFollowHeadSet()
    {
        character.height = xROrigin.CameraInOriginSpaceHeight + additionalHeight;
        Vector3 capsuleCenter = transform.InverseTransformPoint(xROrigin.Camera.transform.position);
        character.center = new Vector3(capsuleCenter.x, (character.height / 2) + character.skinWidth, capsuleCenter.z);
        groundPos = this.transform.TransformPoint(character.center - new Vector3(0, character.center.y, 0));
        GroundContact.transform.position = groundPos;
    }

    private bool CheckIfGrounded()
    {
        Vector3 rayStart = transform.TransformPoint(character.center);
        float rayLength = character.center.y + 0.01f;
        bool hasHit = Physics.SphereCast(rayStart, character.radius, Vector3.down, out RaycastHit hitInfo, rayLength, groundLayer);
        sphereCastHitPoint = hitInfo.point;
        return hasHit;
    }
}
