using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Xamarin.Forms;
using Media.Plugin;
using PropertyChanged;
using Media.Plugin.Abstractions;

namespace XamFormsTest
{
    [ImplementPropertyChanged]
    public class PhotoTestVM
    {
        public PhotoTestVM()
        {
            TakePicCommand = new Command(async (nothing) =>
            {
                // execute code
                var pic = await CrossMedia.Current.TakePhotoAsync(new StoreCameraMediaOptions());
                if (pic != null)
                {
                    SelectedImage = ImageSource.FromStream(() =>
                        {
                            var stream = pic.GetStream();
                            pic.Dispose();
                            return stream;
                        });
                }
            },
            (nothing) =>
            {
                // can execute code
                return CrossMedia.Current.IsCameraAvailable && CrossMedia.Current.IsTakePhotoSupported;
            });

            ChoosePicCommand = new Command(async (nothing) =>
            {
                // execute code
                var pic = await CrossMedia.Current.PickPhotoAsync();
                if (pic != null)
                {
                    SelectedImage = ImageSource.FromStream(() =>
                        {
                            var stream = pic.GetStream();
                            pic.Dispose();
                            return stream;
                        });
                }
            },
            (nothing) =>
            {
                // can execute code
                return CrossMedia.Current.IsPickPhotoSupported;
            });
        }
        public ICommand TakePicCommand { get; protected set; }
        public ICommand ChoosePicCommand { get; protected set; }
        public ImageSource SelectedImage { get; set; }
    }
}
