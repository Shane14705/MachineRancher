﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MachineRancher
{
    [AttributeUsage(AttributeTargets.Class, Inherited =false)]
    public class DiscoveryTopicAttribute : Attribute
    {
        public string path;
        static int count = 0;

        /// <summary>
        /// Tells the rancher what MQTT path to watch for new devices of this type.
        /// Hint: Path should be topics at the root level, with individual machines being published as direct subtopics to this root topic.
        /// I.E: Devices/, Printers/, etc. Racher will then look for things like Devices/Device1, Printers/Printer2, etc.
        /// </summary>
        /// <param name="path">MQTT Path to watch</param>
        public DiscoveryTopicAttribute(string path)
        {
            this.path = path;
        }

    }

    [AttributeUsage(AttributeTargets.Property)]
    public class MonitorRegistrationAttribute : Attribute
    {
        public string topic;
        public int samples_to_average;
        public bool is_json;
        public string json_key;

        private List<Object> sample_list = new List<Object>();
        private int current_index = 0;

        /// <summary>
        /// ONLY SUPPORTS FLOATS, STRINGS, INTS
        /// Registers a property to be updated via MQTT. (The type, ie: whether or not we are dealing with an array, is found by the property's type.
        /// </summary>
        /// <param name="topic">The topic this property is to be set using. Use a '*' in places where the machine's name would be.</param>
        /// <param name="dict_key">If the mqtt topic gives a JSON object, this is the key we will use to grab the value. Defaults to "value".</param>
        /// <param name="samples_to_average">The number of samples to buffer and average before updating the property. Defaults to no buffering.</param>
        public MonitorRegistrationAttribute(string topic, string dict_key, int samples_to_average=1)
        {
            this.is_json = true;
            this.topic = topic;
            this.json_key = dict_key;
            this.samples_to_average = samples_to_average;
        }

        public float sample_property(float new_sample)
        {
            if (this.current_index >= samples_to_average)
            {
                this.current_index = 0;
            }
        
            if (this.sample_list.Count < samples_to_average)
            {
                this.sample_list.Add(new_sample);
            }
            else
            {
                this.sample_list[this.current_index] = new_sample;
                this.current_index++;
            }

            float output = 0;
            foreach (var item in this.sample_list)
            {
                output += (float)item;
            }

            output /= this.sample_list.Count;
            return output;
        }

        public int sample_property(int new_sample)
        {
            if (this.current_index >= samples_to_average)
            {
                this.current_index = 0;
            }

            if (this.sample_list.Count < samples_to_average)
            {
                this.sample_list.Add(new_sample);
            }
            else
            {
                this.sample_list[this.current_index] = new_sample;
                this.current_index++;
            }

            int output = 0;
            foreach (var item in this.sample_list)
            {
                output += (int)item;
            }

            output /= this.sample_list.Count;
            return output;
        }

    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MachineFunctionAttribute : Attribute //Eventually, this could have information added to it to show what and how this functionality should be displayed to the client, what parameters it accepts, etc
        //For now, it is just a placeholder until we get the software defined interfaces all the way down
    {

    }

    /// <summary>
    /// Any machine plugins must inherit from this class to be detected. Additionally, each plugin must have a config section in appsettings.ini with the same name as the class.
    /// Classes that inherit from this must have a "DiscoveryTopic" attribute to be detected as well.
    /// Beyond this, you can add any fields you would like to the section, and your plugin will be provided with access to this section.
    /// </summary>
    internal abstract class Machine
    {   
        public abstract string Name { get; set; }  
        public abstract string Description { get; }
        //public abstract string Websocket { get; set; }

        protected IConfigurationSection config;
        public Machine(string name, IConfigurationSection config)
        {
            this.Name = name;
            this.config = config;
        }
    }
}
