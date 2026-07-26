using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using Newtonsoft.Json;

namespace CUETools.Codecs.CommandLine
{
    [JsonObject(MemberSerialization.OptIn)]
    public class EncoderSettings : IAudioEncoderSettings
    {
        #region IAudioEncoderSettings implementation
        [DefaultValue("")]
        [JsonProperty]
        public string Name { get; set; }

        [DefaultValue("")]
        [JsonProperty]
        public string Extension { get; set; }

        [Browsable(false)]
        public Type EncoderType => typeof(AudioEncoder);

        private bool _lossless;
        private bool _verificationRequired;

        [JsonProperty]
        public bool Lossless
        {
            get { return _lossless; }
            set
            {
                _lossless = value;
                // Once a command encoder has been designated for a lossless face, toggling a
                // mutable UI label must never turn off its output verification in the live session.
                if (value)
                    _verificationRequired = true;
            }
        }

        [Browsable(false)]
        [JsonProperty]
        public bool VerificationRequired
        {
            get { return _verificationRequired; }
            set
            {
                // Sticky by design. Old JSON has no field, but setting Lossless=true above migrates
                // it; a later false value cannot downgrade an already-lossless command contract.
                if (value)
                    _verificationRequired = true;
            }
        }

        [Browsable(false)]
        public int Priority => 0;

        [DefaultValue("")]
        [JsonProperty]
        public string SupportedModes { get; set; }

        public string DefaultMode => EncoderMode;

        [Browsable(false)]
        [DefaultValue("")]
        [JsonProperty]
        public string EncoderMode { get; set; }

        [Browsable(false)]
        public AudioPCMConfig PCM { get; set; }

        [Browsable(false)]
        public int BlockSize { get; set; }

        [Browsable(false)]
        [DefaultValue(4096)]
        public int Padding { get; set; }

        public IAudioEncoderSettings Clone()
        {
            return MemberwiseClone() as IAudioEncoderSettings;
        }
        #endregion

        public EncoderSettings()
        {
            this.Init();
        }

        public EncoderSettings(
            string name,
            string extension,
            bool lossless,
            string supportedModes,
            string defaultMode,
            string path,
            string parameters
            )
        {
            this.Init();
            Name = name;
            Extension = extension;
            Lossless = lossless;
            SupportedModes = supportedModes;
            Path = path;
            EncoderMode = defaultMode;
            Parameters = parameters;
        }

        [DefaultValue("")]
        [JsonProperty]
        public string Path
        {
            get;
            set;
        }

        /// <summary>
        /// Runtime-only identity supplied by a host after it validates an app-managed executable.
        /// These fields are deliberately not JSON properties: approval receipts remain host-owned,
        /// and a persisted settings blob must not be able to grant itself an approved identity.
        /// AudioEncoder rechecks this identity through a read-only handle immediately before launch
        /// and retains that deny-write/delete lease through any self-verification.
        /// </summary>
        [Browsable(false)]
        public string ApprovedExecutableSha256
        {
            get;
            set;
        }

        [Browsable(false)]
        public long ApprovedExecutableLength
        {
            get;
            set;
        }

        [DefaultValue("")]
        [JsonProperty]
        public string Parameters
        {
            get;
            set;
        }

        [DefaultValue(false)]
        [DisplayName("Verify with encoder")]
        [Description("Use the exact encoder executable to decode and independently verify lossless output.")]
        [JsonProperty]
        public bool VerificationUsesEncoder
        {
            get;
            set;
        }

        [DefaultValue("")]
        [DisplayName("Verification decoder")]
        [Description("Decoder executable used only for independent lossless verification. Leave empty when 'Verify with encoder' is enabled.")]
        [JsonProperty]
        public string VerificationPath
        {
            get;
            set;
        }

        [DefaultValue("")]
        [DisplayName("Verification arguments")]
        [Description("Arguments that decode %I to a WAV stream on standard output. Required for every command-line lossless encoder.")]
        [JsonProperty]
        public string VerificationParameters
        {
            get;
            set;
        }

        [Browsable(false)]
        public bool HasLosslessVerifier
        {
            get
            {
                if (!VerificationRequired)
                    return true;
                if (String.IsNullOrEmpty(VerificationParameters) ||
                    !VerificationParameters.Contains("%I"))
                    return false;
                return VerificationUsesEncoder ^
                    !String.IsNullOrEmpty(VerificationPath);
            }
        }

        [DefaultValue(600000)]
        [DisplayName("Process timeout (ms)")]
        [Description("Maximum time an external encoder may make no input or exit progress. The encoder process is terminated when this limit is reached.")]
        [JsonProperty]
        public int ProcessTimeoutMilliseconds
        {
            get;
            set;
        }
    }
}
