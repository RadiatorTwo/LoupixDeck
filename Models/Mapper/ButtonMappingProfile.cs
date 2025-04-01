using AutoMapper;
using LoupixDeck.Utils;

namespace LoupixDeck.Models.Mapper;

public class ButtonMappingProfile : Profile
{
    public ButtonMappingProfile()
    {
        CreateMap<TouchButton, TouchButton>()
            .ForMember(dest => dest.Image, opt => opt.MapFrom(src => src.Image.CloneBitmap()))
            .ForMember(dest => dest.RenderedImage, opt => opt.MapFrom(src => src.RenderedImage.CloneBitmap()));
    }
}