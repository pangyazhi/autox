#region

// Hapa Project, CC
// Created @2012 09 18 14:34
// Last Updated  by Huang, Jien @2012 09 18 14:34

using System;

#region

using System.Activities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Linq;
using AutoX.Basic;

#endregion

#endregion

namespace AutoX.Activities.AutoActivities
{
    [ToolboxBitmap(typeof (TestSuiteDesigner), "TestSuite.png")]
    [Designer(typeof (TestSuiteDesigner))]
    public sealed class TestSuiteActivity : AutomationActivity, IPassData
    {
        private readonly Variable<int> _currentIndex;
        private string _name;
        private CompletionCallback _onChildComplete;

        public TestSuiteActivity()
        {
            children = new Collection<Activity>();
            _currentIndex = new Variable<int>();
            ErrorLevel = OnError.Continue;
        }


        [Browsable(false)]
        public string GUID { get; set; }

        public new string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                DisplayName = "Suite: " + _name;
            }
        }
        
        [DisplayName(@"Maturity")]
        [DefaultValue(MaturityLevel.Playground)]
        public MaturityLevel Maturity { get; set; }

        [DisplayName(@"On Error")]
        [DefaultValue(OnError.Continue)]
        public OnError ErrorLevel { get; set; }

        [Browsable(false)]
        public Collection<Activity> children { get; set; }

        #region IPassData Members

        public void PassData(string instanceId, string outerData)
        {
            InstanceId = instanceId;
        }

        #endregion

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            //call base.CacheMetadata to add the Activities and Variables to this activity's metadata
            base.CacheMetadata(metadata);
            //add the private implementation variable: currentIndex 
            metadata.AddImplementationVariable(_currentIndex);
            string errorMessage = AutomationActivityValidation();
            if (!string.IsNullOrEmpty(errorMessage))
                metadata.AddValidationError(errorMessage);
            
        }

        public override string AutomationActivityValidation()
        {
            if (children.Count <= 0)
                return "No child activity, this test suite is useless";
            foreach (var child in children)
            {
                var ac = child as AutomationActivity;
                if (ac != null)
                {
                   var message = ac.AutomationActivityValidation();
                   if (!string.IsNullOrEmpty(message))
                       return message;
                }
            }

            return base.AutomationActivityValidation();
        }
        protected override void Execute(NativeActivityContext context)
        {
            //set env environment variables
            var steps =
                XElement.Parse("<AutoX.Steps  OnError=\"" + ErrorLevel + "\" InstanceId=\"" + InstanceId + "\"/>");
            var setEnv = XElement.Parse("<Step />");
            setEnv.SetAttributeValue(Constants.ACTION, Constants.SET_ENV);
            
            //Description += "\nProperties:\n";
            foreach (PropertyDescriptor propertyDescriptor in context.DataContext.GetProperties())
            {
                setEnv.SetAttributeValue(propertyDescriptor.Name, propertyDescriptor.GetValue(context.DataContext));
                //Description += propertyDescriptor.Name + " : " +
                //               propertyDescriptor.GetValue(context.DataContext) + ";";
            }
            var _config = Host.GetConfig();
            //Description += "\nVariables From Configuration:\n";
            foreach (var variable in _config.GetList())
            {
                if(variable.Key.Contains(":"))
                    continue;
                try
                {
                    setEnv.SetAttributeValue(variable.Key, variable.Value);
                    //Description += variable.Key + " : " + variable.Value + ";";
                }
                catch (Exception e)
                {
                    Log.Warn(ExceptionHelper.FormatStackTrace("Set Env attributes setting failed: key["+variable.Key+"] value["+variable.Value+"]",e));
                }
                
            }
            //Description += "\n";
            steps.Add(setEnv);
            Host.SetCommand(steps);
            Host.GetResult();
            //add a result level here
            Result = new XElement(Constants.RESULT);
            SetResult();
            SetVariablesBeforeRunning(context);
            InternalExecute(context, null);
            SetFinalResult("Maturity",Maturity.ToString());
        }

        private void InternalExecute(NativeActivityContext context, ActivityInstance instance)
        {
            //this method turn async way to sync.
            
            //grab the index of the current Activity
            var currentActivityIndex = _currentIndex.Get(context);
            if (currentActivityIndex > 0)
            {
                var lastChild = children[currentActivityIndex - 1];
                //Get result here, it is sync or async????
                RunningResult = RunningResult && ((IPassData)lastChild).GetResult();
                
                if (!RunningResult)
                {
                    if (ErrorLevel == OnError.AlwaysReturnTrue)
                        RunningResult = true;
                    if (ErrorLevel == OnError.Terminate)
                    {
                        Log.Fatal("Workflow terminated according OnError.Terminate");
                        context.Abort();
                    }
                    if (ErrorLevel == OnError.Continue)
                    {
                        //do nothing, just continue
                    }
                    if (ErrorLevel == OnError.JustShowWarning)
                    {
                        Log.Warn("Warning:\n" + lastChild.DisplayName + " Error happened, but we ignore it");
                        RunningResult = true;
                    }
                    if (ErrorLevel == OnError.StopCurrentScript)
                    {
                        Log.Error("Error:\n" + lastChild.DisplayName + " Error happened, stop current script.");
                        return;
                    }
                }
                //set variables value ((AutomationActivity)nextChild).Name to _runningResult
                AddVariable(lastChild.DisplayName.Replace(" ", "_").Replace(":", "_"), RunningResult.ToString());
                SetVariableValueByContext(context, lastChild.DisplayName.Replace(" ", "_").Replace(":", "_"), RunningResult.ToString());
            }
            if (currentActivityIndex == children.Count)
            {
                //if the currentActivityIndex is equal to the count of MySequence's Activities
                //Suite is complete, then we close the browser here
                //TODO please reconsider here, this means: suite must be totally independent, it will open & close browser itself. Then if it is called by others, must be very careful!!!!!!
                var steps =
                    XElement.Parse("<AutoX.Steps  OnError=\"" + ErrorLevel + "\" InstanceId=\"" + InstanceId + "\"/>");
                var close = XElement.Parse("<Step Action=\"Close\" />");
                steps.Add(close);
                Host.SetCommand(steps);
                Host.GetResult();
                return;
            }

            if (_onChildComplete == null)
            {
                //on completion of the current child, have the runtime call back on this method
                _onChildComplete = InternalExecute;
            }

            //grab the next Activity in MySequence.Activities and schedule it
            var nextChild = children[currentActivityIndex];
            var childEnabled = false;
            var child = nextChild as AutomationActivity;
            if (child != null)
            {
                child.SetHost(Host);
                child.InstanceId = InstanceId;
                child.SetParentResultId(ResultId);
                childEnabled = child.Enabled;
            }
            //if enabled, run it, don't think to use while, it is a recursive, async cycle here!
            if (childEnabled)
            {
                context.ScheduleActivity(nextChild, _onChildComplete);
                
            }
            //increment the currentIndex
            _currentIndex.Set(context, ++currentActivityIndex);
        }

        
    }


}